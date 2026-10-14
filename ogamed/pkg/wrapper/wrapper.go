package wrapper

import (
	"github.com/alaingilbert/ogame/pkg/ogame"
	"time"
)

type IntoCoordinate any

type IntoCelestial any

type IntoPlanet any

type IntoMoon any

type Options struct {
	DebugGalaxy       bool
	SkipInterceptor   bool
	SkipRetry         bool
	SkipCacheFullPage bool
	ChangePlanet      ogame.CelestialID
	Delay             time.Duration
}

type Option func(*Options)

func DebugGalaxy(opt *Options) {
	opt.DebugGalaxy = true
}

func SkipInterceptor(opt *Options) {
	opt.SkipInterceptor = true
}

func SkipRetry(opt *Options) {
	opt.SkipRetry = true
}

func SkipCacheFullPage(opt *Options) {
	opt.SkipCacheFullPage = true
}

func ChangePlanet(celestialID ogame.CelestialID) Option {
	return func(opt *Options) {
		opt.ChangePlanet = celestialID
	}
}

func Delay(dur time.Duration) Option {
	return func(opt *Options) {
		opt.Delay = dur
	}
}
