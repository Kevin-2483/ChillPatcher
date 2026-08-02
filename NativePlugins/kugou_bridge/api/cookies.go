package api

import (
	"errors"
	"net/http"
	"net/http/cookiejar"
	"net/url"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"time"
)

const cookieFileName = "kugou_cookies.json"

type storedCookie struct {
	Origin   string        `json:"origin"`
	Name     string        `json:"name"`
	Value    string        `json:"value"`
	Path     string        `json:"path,omitempty"`
	Domain   string        `json:"domain,omitempty"`
	Expires  int64         `json:"expires,omitempty"`
	MaxAge   int           `json:"maxAge,omitempty"`
	Secure   bool          `json:"secure,omitempty"`
	HTTPOnly bool          `json:"httpOnly,omitempty"`
	SameSite http.SameSite `json:"sameSite,omitempty"`
}

type persistentCookieJar struct {
	jar     http.CookieJar
	path    string
	mu      sync.Mutex
	records map[string]storedCookie
	loading bool
}

func newPersistentCookieJar(dataDir string, existing http.CookieJar) (*persistentCookieJar, error) {
	if existing == nil {
		created, err := cookiejar.New(nil)
		if err != nil {
			return nil, storageError("loadCookies", err)
		}
		existing = created
	}
	result := &persistentCookieJar{
		jar: existing, path: filepath.Join(dataDir, cookieFileName), records: map[string]storedCookie{}, loading: true,
	}
	var saved []storedCookie
	if err := readJSON(result.path, &saved); err != nil && !errors.Is(err, os.ErrNotExist) {
		return nil, storageError("loadCookies", err)
	}
	now := time.Now()
	for _, record := range saved {
		origin, err := url.Parse(record.Origin)
		if err != nil || origin.Host == "" || record.Name == "" || (record.Expires > 0 && time.Unix(record.Expires, 0).Before(now)) {
			continue
		}
		cookie := record.cookie()
		result.jar.SetCookies(origin, []*http.Cookie{cookie})
		result.records[cookieRecordKey(record)] = record
	}
	result.loading = false
	return result, nil
}

func (j *persistentCookieJar) Cookies(target *url.URL) []*http.Cookie {
	return j.jar.Cookies(target)
}

func (j *persistentCookieJar) SetCookies(origin *url.URL, cookies []*http.Cookie) {
	j.jar.SetCookies(origin, cookies)
	if origin == nil || origin.Host == "" || len(cookies) == 0 {
		return
	}
	j.mu.Lock()
	defer j.mu.Unlock()
	now := time.Now()
	for _, cookie := range cookies {
		if cookie == nil || strings.TrimSpace(cookie.Name) == "" {
			continue
		}
		record := storedCookie{
			Origin: origin.Scheme + "://" + origin.Host, Name: cookie.Name, Value: cookie.Value,
			Path: cookie.Path, Domain: cookie.Domain, MaxAge: cookie.MaxAge,
			Secure: cookie.Secure, HTTPOnly: cookie.HttpOnly, SameSite: cookie.SameSite,
		}
		if !cookie.Expires.IsZero() {
			record.Expires = cookie.Expires.Unix()
		}
		key := cookieRecordKey(record)
		if cookie.MaxAge < 0 || (!cookie.Expires.IsZero() && cookie.Expires.Before(now)) {
			delete(j.records, key)
			continue
		}
		j.records[key] = record
	}
	if !j.loading {
		_ = j.saveLocked()
	}
}

func (j *persistentCookieJar) saveLocked() error {
	records := make([]storedCookie, 0, len(j.records))
	now := time.Now().Unix()
	for key, record := range j.records {
		if record.Expires > 0 && record.Expires < now {
			delete(j.records, key)
			continue
		}
		records = append(records, record)
	}
	return writeJSON(j.path, records)
}

func (r storedCookie) cookie() *http.Cookie {
	cookie := &http.Cookie{
		Name: r.Name, Value: r.Value, Path: r.Path, Domain: r.Domain, MaxAge: r.MaxAge,
		Secure: r.Secure, HttpOnly: r.HTTPOnly, SameSite: r.SameSite,
	}
	if r.Expires > 0 {
		cookie.Expires = time.Unix(r.Expires, 0)
	}
	return cookie
}

func cookieRecordKey(record storedCookie) string {
	return strings.ToLower(record.Origin + "|" + record.Domain + "|" + record.Path + "|" + record.Name)
}
