package main

import (
	"encoding/json"
	"net/http"
	"os"
	"path/filepath"
	"regexp"

	"github.com/labstack/echo/v4"
)

var tileTexts = map[string]map[string]string{
	"de": {
		"Pause":                    "Pause",
		"Resume":                   "Fortsetzen",
		"Tiles":                    "Kacheln",
		"on":                       "an",
		"off":                      "aus",
		"AutoFarm override active": "AutoFarm-Sondermodus aktiv",
		"INCOMING ATTACK":          "ANGRIFF UNTERWEGS",
		"in":                       "in",
		"and {n} more":             "und {n} weitere",
		"Ghost sleep on":           "Ghost-Schlaf an",
		"Sleep mode on":            "Schlafmodus an",
		"Recalls due":              "Rückrufe fällig",
		"Wake up in":               "Aufwachen in",
		"Back to sleep in":         "Schlafen in",
		"ACS":                      "Verbandsangriff",
		"Missile":                  "Raketen",
		"Destroy":                  "Zerstörung",
		"Attack":                   "Angriff",
		"Slots":                    "Slots",
		"free":                     "frei",
		"ok":                       "ok",
		"Error":                    "Fehler",
		"Keep free":                "Frei halten",
		"Exp":                      "Exp",
		"Workers (priority: lower = more important)": "Worker (Priorität: kleiner = wichtiger)",
		"Slot priority (lower = more important)":     "Slot-Priorität (kleiner = wichtiger)",
		"On":                                         "An",
		"Off":                                        "Aus",
		"Cargo build":                                "Frachterbau",
		"Mines":                                      "Minen",
		"Research":                                   "Forschung",
		"LF mines":                                   "LF-Minen",
		"LF research":                                "LF-Forschung",
		"Daily offer":                                "Tagesangebot",
		"Colonizer":                                  "Kolonisierer",
	},
}

var langCode = regexp.MustCompile(`^[a-z]{2,3}(-[A-Za-z]{2,4})?$`)

func TileTextsHandler(c echo.Context) error {
	lang := c.Param("lang")
	out := map[string]string{}
	if !langCode.MatchString(lang) {
		return c.JSON(http.StatusOK, out)
	}
	for k, v := range tileTexts[lang] {
		out[k] = v
	}
	if exe, err := os.Executable(); err == nil {
		if data, err := os.ReadFile(filepath.Join(filepath.Dir(exe), "lang", lang+".json")); err == nil {
			extra := map[string]string{}
			if json.Unmarshal(data, &extra) == nil {
				for k, v := range extra {
					out[k] = v
				}
			}
		}
	}
	return c.JSON(http.StatusOK, out)
}
