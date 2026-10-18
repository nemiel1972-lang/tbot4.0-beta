package main

import (
	"os"
	"path/filepath"
	"strings"

	"github.com/urfave/cli/v3"
)

func tlsFlags() []cli.Flag {
	return []cli.Flag{
		&cli.BoolFlag{
			Name:    "enable-tls",
			Usage:   "Enable TLS. Needs key.pem and cert.pem",
			Sources: cli.EnvVars("OGAMED_ENABLE_TLS"),
		},
		&cli.StringFlag{
			Name:    "tls-key-file",
			Usage:   "Path to key.pem",
			Value:   "~/.ogame/key.pem",
			Sources: cli.EnvVars("OGAMED_TLS_KEYFILE"),
		},
		&cli.StringFlag{
			Name:    "tls-cert-file",
			Usage:   "Path to cert.pem",
			Value:   "~/.ogame/cert.pem",
			Sources: cli.EnvVars("OGAMED_TLS_CERTFILE"),
		},
	}
}

func expandHome(p string) string {
	if p != "~" && !strings.HasPrefix(p, "~/") && !strings.HasPrefix(p, "~"+string(os.PathSeparator)) {
		return p
	}
	home, err := os.UserHomeDir()
	if err != nil || home == "" {
		return p
	}
	if p == "~" {
		return home
	}
	return filepath.Join(home, p[2:])
}
