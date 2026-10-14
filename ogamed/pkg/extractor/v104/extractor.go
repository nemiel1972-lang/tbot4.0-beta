package v104

import (
	v10 "github.com/alaingilbert/ogame/pkg/extractor/v10"
)

type Extractor struct {
	v10.Extractor
}

func NewExtractor() *Extractor {
	return &Extractor{}
}

func (e *Extractor) ExtractUpgradeToken(pageHTML []byte) (string, error) {
	return extractUpgradeToken(pageHTML)
}

func (e *Extractor) ExtractTearDownToken(pageHTML []byte) (string, error) {
	return extractTearDownToken(pageHTML)
}

func (e *Extractor) ExtractToken(pageHTML []byte) (string, error) {
	return ExtractToken(pageHTML)
}
