package exponentialBackoff

import (
	"context"
	"github.com/alaingilbert/clockwork"
	"github.com/stretchr/testify/assert"
	"sync"
	"sync/atomic"
	"testing"
	"time"
)

func TestExponentialBackoff_Wait(t *testing.T) {
	var counter uint32
	clock := clockwork.NewFakeClock()
	wg := &sync.WaitGroup{}
	wg.Add(1)
	go func() {
		clock.BlockUntil(1)
		clock.Advance(1000 * time.Millisecond)
		clock.BlockUntil(0)
		clock.BlockUntil(1)
		clock.Advance(2000 * time.Millisecond)
		clock.BlockUntil(0)
		clock.BlockUntil(1)
		clock.Advance(4000 * time.Millisecond)
		clock.BlockUntil(0)
		clock.BlockUntil(1)
		clock.Advance(8000 * time.Millisecond)
		clock.BlockUntil(0)
		atomic.AddUint32(&counter, 1)
		wg.Done()
	}()
	e := NewWithClock(context.Background(), clock, 60)
	e.Wait()
	e.Wait()
	e.Wait()
	e.Wait()
	wg.Wait()
	assert.Equal(t, uint32(1), atomic.LoadUint32(&counter))
}

