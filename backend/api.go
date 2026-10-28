package main

import (
	"encoding/json"
	"errors"
	"io"
	"log"
	"mime"
	"net/http"
	"strings"
)

type api struct{ store *issueStore }

func newAPI(store *issueStore) http.Handler { return &api{store: store} }

func writeJSON(w http.ResponseWriter, status int, value any) {
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	w.WriteHeader(status)
	if err := json.NewEncoder(w).Encode(value); err != nil {
		log.Printf("encode response: %v", err)
	}
}

func writeError(w http.ResponseWriter, status int, code, message string) {
	writeJSON(w, status, map[string]any{"error": map[string]string{"code": code, "message": message}})
}

func decodeJSON(w http.ResponseWriter, r *http.Request, target any) bool {
	if contentType := r.Header.Get("Content-Type"); contentType != "" {
		mediaType, _, err := mime.ParseMediaType(contentType)
		if err != nil || mediaType != "application/json" {
			writeError(w, http.StatusUnsupportedMediaType, "unsupported_media_type", "Content-Type must be application/json")
			return false
		}
	}
	r.Body = http.MaxBytesReader(w, r.Body, 1<<20)
	defer r.Body.Close()
	decoder := json.NewDecoder(r.Body)
	decoder.DisallowUnknownFields()
	err := decoder.Decode(target)
	if err == nil {
		var extra any
		if trailing := decoder.Decode(&extra); trailing != io.EOF {
			err = trailing
			if err == nil {
				err = errors.New("multiple JSON values")
			}
		}
	}
	if err != nil {
		var tooLarge *http.MaxBytesError
		if errors.As(err, &tooLarge) {
			writeError(w, http.StatusRequestEntityTooLarge, "body_too_large", "request body exceeds 1 MiB")
		} else {
			writeError(w, http.StatusBadRequest, "invalid_json", "body must be one JSON object with valid field names and types: "+err.Error())
		}
		return false
	}
	return true
}

func (a *api) ServeHTTP(w http.ResponseWriter, r *http.Request) {
	// Development CORS: React may use localhost or a LAN host on any port.
	// Credentials are not enabled. Restrict origins before public deployment.
	w.Header().Set("Access-Control-Allow-Origin", "*")
	w.Header().Set("Access-Control-Allow-Methods", "GET, POST, PATCH, OPTIONS")
	w.Header().Set("Access-Control-Allow-Headers", "Content-Type")
	if r.Method == http.MethodOptions {
		w.WriteHeader(http.StatusNoContent)
		return
	}
	allowed := ""
	switch {
	case r.URL.Path == "/api/health":
		allowed = "GET, OPTIONS"
		if r.Method == http.MethodGet {
			writeJSON(w, http.StatusOK, map[string]string{"status": "ok"})
			return
		}
	case r.URL.Path == "/api/issues":
		allowed = "GET, POST, OPTIONS"
		switch r.Method {
		case http.MethodGet:
			writeJSON(w, http.StatusOK, a.store.list())
			return
		case http.MethodPost:
			a.create(w, r)
			return
		}
	default:
		parts := strings.Split(strings.TrimPrefix(r.URL.Path, "/"), "/")
		if len(parts) == 4 && parts[0] == "api" && parts[1] == "issues" && parts[2] != "" && parts[3] == "status" {
			allowed = "PATCH, OPTIONS"
			if r.Method == http.MethodPatch {
				a.updateStatus(w, r, parts[2])
				return
			}
		}
	}
	if allowed != "" {
		w.Header().Set("Allow", allowed)
		writeError(w, http.StatusMethodNotAllowed, "method_not_allowed", "HTTP method is not supported for this endpoint")
		return
	}
	writeError(w, http.StatusNotFound, "not_found", "endpoint not found")
}

func (a *api) create(w http.ResponseWriter, r *http.Request) {
	var input struct {
		Title       *string `json:"title"`
		Description string  `json:"description"`
		Priority    string  `json:"priority"`
		Position    *struct {
			X *float64 `json:"x"`
			Y *float64 `json:"y"`
			Z *float64 `json:"z"`
		} `json:"position"`
	}
	if !decodeJSON(w, r, &input) {
		return
	}
	if input.Title == nil || strings.TrimSpace(*input.Title) == "" {
		writeError(w, http.StatusBadRequest, "validation_error", "title is required and must not be blank")
		return
	}
	if !validPriority(input.Priority) {
		writeError(w, http.StatusBadRequest, "validation_error", "priority must be low, medium or high")
		return
	}
	p := input.Position
	if p == nil || p.X == nil || p.Y == nil || p.Z == nil || !finite(*p.X) || !finite(*p.Y) || !finite(*p.Z) {
		writeError(w, http.StatusBadRequest, "validation_error", "position must contain numeric x, y and z")
		return
	}
	issue, err := a.store.create(strings.TrimSpace(*input.Title), input.Description, input.Priority, Position{X: *p.X, Y: *p.Y, Z: *p.Z})
	if err != nil {
		log.Printf("create issue: %v", err)
		writeError(w, http.StatusInternalServerError, "storage_error", "could not save issue")
		return
	}
	writeJSON(w, http.StatusCreated, issue)
}

func (a *api) updateStatus(w http.ResponseWriter, r *http.Request, id string) {
	var input struct {
		Status string `json:"status"`
	}
	if !decodeJSON(w, r, &input) {
		return
	}
	if !validStatus(input.Status) {
		writeError(w, http.StatusBadRequest, "validation_error", "status must be open, in_progress or resolved")
		return
	}
	issue, err := a.store.updateStatus(id, input.Status)
	if errors.Is(err, errNotFound) {
		writeError(w, http.StatusNotFound, "issue_not_found", "issue ID does not exist")
		return
	}
	if err != nil {
		log.Printf("update issue: %v", err)
		writeError(w, http.StatusInternalServerError, "storage_error", "could not save issue status")
		return
	}
	writeJSON(w, http.StatusOK, issue)
}
