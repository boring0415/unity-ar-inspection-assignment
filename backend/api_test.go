package main

import (
	"encoding/json"
	"fmt"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"testing"
)

const validBody = `{"title":"入口墙面破损","description":"左侧墙体存在裂缝","priority":"high","position":{"x":0.42,"y":0.03,"z":1.26}}`

func setup(t *testing.T) (*issueStore, http.Handler) {
	t.Helper()
	s, err := newStore(filepath.Join(t.TempDir(), "data", "issues.json"))
	if err != nil {
		t.Fatal(err)
	}
	return s, newAPI(s)
}

func request(handler http.Handler, method, path, body string) *httptest.ResponseRecorder {
	r := httptest.NewRequest(method, path, strings.NewReader(body))
	r.Header.Set("Content-Type", "application/json")
	w := httptest.NewRecorder()
	handler.ServeHTTP(w, r)
	return w
}

func assertStatus(t *testing.T, response *httptest.ResponseRecorder, status int) {
	t.Helper()
	if response.Code != status {
		t.Fatalf("status = %d, want %d; body: %s", response.Code, status, response.Body.String())
	}
}

func TestIssueLifecycleAndPersistence(t *testing.T) {
	s, handler := setup(t)
	empty := request(handler, "GET", "/api/issues", "")
	assertStatus(t, empty, 200)
	if strings.TrimSpace(empty.Body.String()) != "[]" {
		t.Fatalf("empty list: %s", empty.Body.String())
	}
	created := request(handler, "POST", "/api/issues", validBody)
	assertStatus(t, created, 201)
	var issue Issue
	if err := json.Unmarshal(created.Body.Bytes(), &issue); err != nil {
		t.Fatal(err)
	}
	if issue.ID == "" || issue.Status != "open" || issue.Title != "入口墙面破损" ||
		issue.Description != "左侧墙体存在裂缝" || issue.Priority != "high" ||
		issue.Position != (Position{X: .42, Y: .03, Z: 1.26}) ||
		issue.CreatedAt.IsZero() || !issue.UpdatedAt.Equal(issue.CreatedAt) {
		t.Fatalf("unexpected created issue: %+v", issue)
	}
	var fields map[string]json.RawMessage
	json.Unmarshal(created.Body.Bytes(), &fields)
	if len(fields) != 8 {
		t.Fatalf("unexpected response fields: %v", fields)
	}
	for _, status := range []string{"in_progress", "resolved", "open"} {
		updated := request(handler, "PATCH", "/api/issues/"+issue.ID+"/status", `{"status":"`+status+`"}`)
		assertStatus(t, updated, 200)
		var result Issue
		if err := json.Unmarshal(updated.Body.Bytes(), &result); err != nil {
			t.Fatal(err)
		}
		if result.Status != status || result.ID != issue.ID || !result.CreatedAt.Equal(issue.CreatedAt) || !result.UpdatedAt.After(issue.UpdatedAt) {
			t.Fatalf("unexpected update: %+v", result)
		}
		issue = result
	}
	reopened, err := newStore(s.path)
	if err != nil {
		t.Fatal(err)
	}
	listed := request(newAPI(reopened), "GET", "/api/issues", "")
	assertStatus(t, listed, 200)
	var issues []Issue
	if err := json.Unmarshal(listed.Body.Bytes(), &issues); err != nil {
		t.Fatal(err)
	}
	if len(issues) != 1 || issues[0] != issue {
		t.Fatalf("reloaded data differs: %+v", issues)
	}
}

func TestCreateValidation(t *testing.T) {
	_, handler := setup(t)
	cases := []struct{ name, body string }{
		{"blank title", strings.Replace(validBody, "入口墙面破损", "  ", 1)},
		{"missing title", `{"priority":"low","position":{"x":0,"y":0,"z":0}}`},
		{"null title", strings.Replace(validBody, `"入口墙面破损"`, `null`, 1)},
		{"priority", strings.Replace(validBody, `"high"`, `"urgent"`, 1)},
		{"missing priority", `{"title":"a","position":{"x":0,"y":0,"z":0}}`},
		{"missing position", `{"title":"a","priority":"low"}`},
		{"missing coordinate", `{"title":"a","priority":"low","position":{"x":0,"z":0}}`},
		{"null coordinate", strings.Replace(validBody, "0.42", "null", 1)},
		{"string coordinate", strings.Replace(validBody, "0.42", `"0.42"`, 1)},
		{"overflow coordinate", strings.Replace(validBody, "0.42", "1e400", 1)},
		{"bad JSON", `{"title":`},
		{"empty body", ""},
		{"null body", "null"},
		{"array body", "[]"},
		{"multiple objects", validBody + validBody},
		{"trailing garbage", validBody + "broken"},
		{"unknown field", strings.Replace(validBody, `"title":`, `"id":"client-id","title":`, 1)},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			response := request(handler, "POST", "/api/issues", tc.body)
			assertStatus(t, response, 400)
			var payload struct {
				Error struct{ Code, Message string }
			}
			if err := json.Unmarshal(response.Body.Bytes(), &payload); err != nil || payload.Error.Code == "" || payload.Error.Message == "" {
				t.Fatalf("invalid error envelope: %s", response.Body.String())
			}
		})
	}
	listed := request(handler, "GET", "/api/issues", "")
	if strings.TrimSpace(listed.Body.String()) != "[]" {
		t.Fatal("invalid requests changed stored issues")
	}
	for _, priority := range []string{"low", "medium", "high"} {
		assertStatus(t, request(handler, "POST", "/api/issues", `{"title":"zero","priority":"`+priority+`","position":{"x":0,"y":0,"z":0}}`), 201)
	}
}

func TestStatusValidationAndRoutes(t *testing.T) {
	_, handler := setup(t)
	for _, body := range []string{`{"status":"closed"}`, `{}`, `null`, `{"status":12}`, `{"status":"open"} {}`} {
		assertStatus(t, request(handler, "PATCH", "/api/issues/missing/status", body), 400)
	}
	missing := request(handler, "PATCH", "/api/issues/missing/status", `{"status":"resolved"}`)
	assertStatus(t, missing, 404)
	if !strings.Contains(missing.Body.String(), "issue_not_found") {
		t.Fatal(missing.Body.String())
	}
	assertStatus(t, request(handler, "GET", "/api/unknown", ""), 404)
	wrongMethod := request(handler, "DELETE", "/api/issues", "")
	assertStatus(t, wrongMethod, 405)
	if wrongMethod.Header().Get("Allow") == "" {
		t.Fatal("missing Allow header")
	}
	health := request(handler, "GET", "/api/health", "")
	assertStatus(t, health, 200)
	if !strings.Contains(health.Body.String(), `"status":"ok"`) {
		t.Fatal(health.Body.String())
	}
}

func TestCORSAndBodyLimits(t *testing.T) {
	_, handler := setup(t)
	r := httptest.NewRequest("OPTIONS", "/api/issues/a/status", nil)
	r.Header.Set("Origin", "http://localhost:5173")
	r.Header.Set("Access-Control-Request-Method", "PATCH")
	r.Header.Set("Access-Control-Request-Headers", "content-type")
	w := httptest.NewRecorder()
	handler.ServeHTTP(w, r)
	assertStatus(t, w, 204)
	if w.Header().Get("Access-Control-Allow-Origin") != "*" ||
		!strings.Contains(w.Header().Get("Access-Control-Allow-Methods"), "PATCH") ||
		w.Header().Get("Access-Control-Allow-Headers") != "Content-Type" {
		t.Fatalf("CORS headers: %v", w.Header())
	}
	errResponse := request(handler, "POST", "/api/issues", `{}`)
	if errResponse.Header().Get("Access-Control-Allow-Origin") != "*" {
		t.Fatal("error response missing CORS")
	}
	assertStatus(t, request(handler, "POST", "/api/issues", `{"title":"`+strings.Repeat("a", 1<<20)+`"}`), 413)
	r = httptest.NewRequest("POST", "/api/issues", strings.NewReader(validBody))
	r.Header.Set("Content-Type", "text/plain")
	w = httptest.NewRecorder()
	handler.ServeHTTP(w, r)
	assertStatus(t, w, 415)
}

func TestConcurrentRequestsPersistAllIssues(t *testing.T) {
	s, handler := setup(t)
	const count = 24
	var wg sync.WaitGroup
	errors := make(chan string, count)
	for i := 0; i < count; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			created := request(handler, "POST", "/api/issues", validBody)
			if created.Code != 201 {
				errors <- created.Body.String()
				return
			}
			var issue Issue
			if err := json.Unmarshal(created.Body.Bytes(), &issue); err != nil {
				errors <- err.Error()
				return
			}
			updated := request(handler, "PATCH", "/api/issues/"+issue.ID+"/status", `{"status":"resolved"}`)
			if updated.Code != 200 {
				errors <- updated.Body.String()
			}
			request(handler, "GET", "/api/issues", "")
		}()
	}
	wg.Wait()
	close(errors)
	for err := range errors {
		t.Error(err)
	}
	reloaded, err := newStore(s.path)
	if err != nil {
		t.Fatal(err)
	}
	issues := reloaded.list()
	if len(issues) != count {
		t.Fatalf("got %d issues, want %d", len(issues), count)
	}
	ids := map[string]bool{}
	for _, issue := range issues {
		if ids[issue.ID] || issue.Status != "resolved" {
			t.Fatalf("duplicate ID or lost update: %+v", issue)
		}
		ids[issue.ID] = true
	}
}

func TestWriteFailureDoesNotChangeMemory(t *testing.T) {
	s, handler := setup(t)
	created := request(handler, "POST", "/api/issues", validBody)
	assertStatus(t, created, 201)
	original := s.list()[0]
	// Renaming a regular file over this directory fails on Windows and Unix.
	s.path = filepath.Dir(s.path)
	assertStatus(t, request(handler, "POST", "/api/issues", validBody), 500)
	assertStatus(t, request(handler, "PATCH", "/api/issues/"+original.ID+"/status", `{"status":"resolved"}`), 500)
	if got := s.list(); len(got) != 1 || got[0] != original {
		t.Fatal("failed write changed in-memory data")
	}
}

func TestCorruptDataIsNotOverwritten(t *testing.T) {
	for i, content := range []string{"", "broken", "null", `{}`, `[{}]`} {
		t.Run(fmt.Sprint(i), func(t *testing.T) {
			path := filepath.Join(t.TempDir(), "issues.json")
			if err := os.WriteFile(path, []byte(content), 0600); err != nil {
				t.Fatal(err)
			}
			if _, err := newStore(path); err == nil {
				t.Fatal("expected invalid file error")
			}
			got, err := os.ReadFile(path)
			if err != nil || string(got) != content {
				t.Fatal("corrupt file was changed")
			}
		})
	}
}
