package main

import (
	"crypto/rand"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"math"
	"os"
	"path/filepath"
	"runtime"
	"strings"
	"sync"
	"time"
)

type Position struct {
	X float64 `json:"x"`
	Y float64 `json:"y"`
	Z float64 `json:"z"`
}

type Issue struct {
	ID          string    `json:"id"`
	Title       string    `json:"title"`
	Description string    `json:"description"`
	Priority    string    `json:"priority"`
	Status      string    `json:"status"`
	Position    Position  `json:"position"`
	CreatedAt   time.Time `json:"createdAt"`
	UpdatedAt   time.Time `json:"updatedAt"`
}

var errNotFound = errors.New("issue not found")

type issueStore struct {
	mu     sync.RWMutex
	path   string
	issues []Issue
}

func validPriority(value string) bool { return value == "low" || value == "medium" || value == "high" }
func validStatus(value string) bool {
	return value == "open" || value == "in_progress" || value == "resolved"
}

func finite(value float64) bool { return !math.IsNaN(value) && !math.IsInf(value, 0) }

func newStore(path string) (*issueStore, error) {
	if err := os.MkdirAll(filepath.Dir(path), 0755); err != nil {
		return nil, err
	}
	s := &issueStore{path: path, issues: []Issue{}}
	data, err := os.ReadFile(path)
	if errors.Is(err, os.ErrNotExist) {
		if err := s.save(s.issues); err != nil {
			return nil, err
		}
		return s, nil
	}
	if err != nil {
		return nil, err
	}
	if err := json.Unmarshal(data, &s.issues); err != nil {
		return nil, fmt.Errorf("invalid data file (original file preserved): %w", err)
	}
	if s.issues == nil {
		return nil, errors.New("data file must contain a JSON array")
	}
	ids := make(map[string]bool, len(s.issues))
	for _, issue := range s.issues {
		if issue.ID == "" || ids[issue.ID] || strings.TrimSpace(issue.Title) == "" ||
			!validPriority(issue.Priority) || !validStatus(issue.Status) ||
			issue.CreatedAt.IsZero() || issue.UpdatedAt.Before(issue.CreatedAt) {
			return nil, errors.New("invalid saved issue (original file preserved)")
		}
		ids[issue.ID] = true
	}
	return s, nil
}

// All reads and writes share this store's mutex. Run one process per data file.
// Publish the in-memory change only after writing, syncing and closing a temporary
// file and replacing the destination, so failed writes do not report success.
func (s *issueStore) save(issues []Issue) error {
	data, err := json.MarshalIndent(issues, "", "  ")
	if err != nil {
		return err
	}
	f, err := os.CreateTemp(filepath.Dir(s.path), ".issues-*.tmp")
	if err != nil {
		return err
	}
	defer os.Remove(f.Name())
	if _, err = f.Write(append(data, '\n')); err != nil {
		f.Close()
		return err
	}
	if err = f.Sync(); err != nil {
		f.Close()
		return err
	}
	if err = f.Close(); err != nil {
		return err
	}
	// Windows scanners can briefly hold a just-written file without delete
	// sharing. Retry only permission failures involving a regular destination;
	// never delete the old file or mask a permanent failure.
	for attempt := 0; ; attempt++ {
		err = os.Rename(f.Name(), s.path)
		if err == nil || runtime.GOOS != "windows" || !errors.Is(err, os.ErrPermission) || attempt >= 6 {
			return err
		}
		info, statErr := os.Stat(s.path)
		if statErr != nil || !info.Mode().IsRegular() {
			return err
		}
		time.Sleep(time.Duration(10*(1<<attempt)) * time.Millisecond)
	}
}

func (s *issueStore) list() []Issue {
	s.mu.RLock()
	defer s.mu.RUnlock()
	return append([]Issue{}, s.issues...)
}

func (s *issueStore) create(title, description, priority string, position Position) (Issue, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	var id string
	for id == "" {
		var raw [16]byte
		if _, err := rand.Read(raw[:]); err != nil {
			return Issue{}, err
		}
		id = hex.EncodeToString(raw[:])
		for _, issue := range s.issues {
			if issue.ID == id {
				id = ""
				break
			}
		}
	}
	now := time.Now().UTC()
	issue := Issue{ID: id, Title: title, Description: description, Priority: priority,
		Status: "open", Position: position, CreatedAt: now, UpdatedAt: now}
	next := append(append([]Issue{}, s.issues...), issue)
	if err := s.save(next); err != nil {
		return Issue{}, err
	}
	s.issues = next
	return issue, nil
}

func (s *issueStore) updateStatus(id, status string) (Issue, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	for index, issue := range s.issues {
		if issue.ID != id {
			continue
		}
		issue.Status = status
		issue.UpdatedAt = time.Now().UTC()
		if !issue.UpdatedAt.After(s.issues[index].UpdatedAt) {
			issue.UpdatedAt = s.issues[index].UpdatedAt.Add(time.Nanosecond)
		}
		next := append([]Issue{}, s.issues...)
		next[index] = issue
		if err := s.save(next); err != nil {
			return Issue{}, err
		}
		s.issues = next
		return issue, nil
	}
	return Issue{}, errNotFound
}
