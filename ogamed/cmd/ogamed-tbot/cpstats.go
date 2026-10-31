package main

import (
	"fmt"
	"log"
	"sort"
	"strings"
	"time"

	"github.com/alaingilbert/ogame/pkg/wrapper"
)

func init() {
	go func() {
		t := time.NewTicker(30 * time.Minute)
		defer t.Stop()
		for range t.C {
			s := wrapper.SnapshotCP()
			if s.BotSwitch+s.BotSame+s.UserSwitch+s.UserSame+s.HumanClicks+s.BrowserAuto == 0 {
				continue
			}
			type kv struct {
				k string
				v int64
			}
			routes := make([]kv, 0, len(s.BotRoutes))
			for k, v := range s.BotRoutes {
				routes = append(routes, kv{k, v})
			}
			sort.Slice(routes, func(i, j int) bool { return routes[i].v > routes[j].v })
			parts := make([]string, 0, 8)
			for i, r := range routes {
				if i >= 8 {
					break
				}
				parts = append(parts, fmt.Sprintf("%s %d", r.k, r.v))
			}
			log.Printf("[CP] switches 30min: bot %d (same celestial %d), of which right after user activity (<2 min) %d | browser page on other celestial than before %d (same %d) | browser requests: user actions %d, automatic %d | bot switches by request: %s",
				s.BotSwitch, s.BotSame, s.BotOnUser, s.UserSwitch, s.UserSame, s.HumanClicks, s.BrowserAuto, strings.Join(parts, ", "))
		}
	}()
}

