package wrapper

import (
	"sync"
	"testing"
)

func TestGuardedStringConcurrent(t *testing.T) {
	var g guardedString
	var wg sync.WaitGroup
	for i := 0; i < 8; i++ {
		wg.Add(2)
		go func() {
			defer wg.Done()
			for j := 0; j < 1000; j++ {
				g.Set("a")
			}
		}()
		go func() {
			defer wg.Done()
			for j := 0; j < 1000; j++ {
				_ = g.Get()
			}
		}()
	}
	wg.Wait()
	if g.Get() != "a" {
		t.Fatalf("unerwarteter Wert %q", g.Get())
	}
}
