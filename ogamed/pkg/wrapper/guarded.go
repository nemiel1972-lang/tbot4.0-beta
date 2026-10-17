package wrapper

import "sync"

type guardedString struct {
	mu sync.RWMutex
	v  string
}

func (g *guardedString) Get() string {
	g.mu.RLock()
	defer g.mu.RUnlock()
	return g.v
}

func (g *guardedString) Set(v string) {
	g.mu.Lock()
	g.v = v
	g.mu.Unlock()
}
