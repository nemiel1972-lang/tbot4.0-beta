package v10

import (
	v9 "github.com/alaingilbert/ogame/pkg/extractor/v9"
	"github.com/alaingilbert/ogame/pkg/ogame"
)

type Extractor struct {
	v9.Extractor
}

func NewExtractor() *Extractor {
	return &Extractor{}
}

func (e *Extractor) ExtractGalaxyInfos(pageHTML []byte, botPlayerName string, botPlayerID, botPlayerRank int64) (ogame.SystemInfos, error) {
	return extractGalaxyInfos(pageHTML, botPlayerName, botPlayerID, botPlayerRank)
}
