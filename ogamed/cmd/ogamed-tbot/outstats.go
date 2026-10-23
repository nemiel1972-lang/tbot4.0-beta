package main

import (
	"fmt"
	"log"
	"strings"
	"time"

	"github.com/alaingilbert/ogame/pkg/wrapper"
)

func init() {
	go func() {
		t := time.NewTicker(10 * time.Minute)
		defer t.Stop()
		for range t.C {
			entries, total, browser := wrapper.SnapshotOut()
			if total == 0 {
				continue
			}
			parts := make([]string, 0, 15)
			for i, e := range entries {
				if i >= 15 {
					break
				}
				parts = append(parts, fmt.Sprintf("%s %d (Browser %d)", e.Key, e.All, e.Browser))
			}
			log.Printf("[Out/10min] requests to the game: total %d (%.1f/s), of which browser %d, bot %d: %s", total, float64(total)/600.0, browser, total-browser, strings.Join(parts, ", "))
		}
	}()
}
