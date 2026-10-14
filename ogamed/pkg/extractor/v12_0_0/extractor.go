package v12_0_0

import (
	"bytes"
	"github.com/PuerkitoBio/goquery"
	"github.com/alaingilbert/clockwork"
	"github.com/alaingilbert/ogame/pkg/extractor/v11_15_0"
	"github.com/alaingilbert/ogame/pkg/ogame"
	"time"
)

type Extractor struct {
	v11_15_0.Extractor
}

func NewExtractor() *Extractor {
	return &Extractor{}
}

func (e *Extractor) ExtractServerTime(pageHTML []byte) (time.Time, error) {
	clock := clockwork.NewRealClock()
	return e.extractServerTime(pageHTML, clock)
}

func (e *Extractor) extractServerTime(pageHTML []byte, clock clockwork.Clock) (time.Time, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return time.Time{}, err
	}
	return extractServerTimeFromDoc(doc, clock)
}

func (e *Extractor) ExtractServerTimeFromDoc(doc *goquery.Document) (time.Time, error) {
	clock := clockwork.NewRealClock()
	return extractServerTimeFromDoc(doc, clock)
}

func (e *Extractor) ExtractHighscore(pageHTML []byte) (ogame.Highscore, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return ogame.Highscore{}, err
	}
	return e.ExtractHighscoreFromDoc(doc)
}

func (e *Extractor) ExtractHighscoreFromDoc(doc *goquery.Document) (ogame.Highscore, error) {
	return extractHighscoreFromDoc(doc)
}

func (e *Extractor) ExtractChapter(pageHTML []byte) (ogame.Chapter, error) {
	doc, err := goquery.NewDocumentFromReader(bytes.NewReader(pageHTML))
	if err != nil {
		return ogame.Chapter{}, err
	}
	return e.ExtractChapterFromDoc(doc)
}

func (e *Extractor) ExtractChapterFromDoc(doc *goquery.Document) (ogame.Chapter, error) {
	return extractChapterFromDoc(doc)
}
