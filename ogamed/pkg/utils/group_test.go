package utils

import (
	"regexp"
	"testing"
)

func TestGroup(t *testing.T) {
	r := regexp.MustCompile(`id=(\d+)`)
	if got := Group(r.FindStringSubmatch("id=42"), 1); got != "42" {
		t.Fatalf("erwartet 42, bekam %q", got)
	}
	if got := Group(r.FindStringSubmatch("nichts"), 1); got != "" {
		t.Fatalf("erwartet leer, bekam %q", got)
	}
	if got := Group([]string{"a"}, 5); got != "" {
		t.Fatalf("erwartet leer, bekam %q", got)
	}
}

func TestGroupB(t *testing.T) {
	r := regexp.MustCompile(`id=(\d+)`)
	if got := GroupB(r.FindSubmatch([]byte("id=7")), 1); string(got) != "7" {
		t.Fatalf("erwartet 7, bekam %q", got)
	}
	if got := GroupB(r.FindSubmatch([]byte("x")), 1); got != nil {
		t.Fatalf("erwartet nil, bekam %q", got)
	}
	if ToInt(GroupB(nil, 1)) != 0 {
		t.Fatal("ToInt(nil) muss 0 sein")
	}
}
