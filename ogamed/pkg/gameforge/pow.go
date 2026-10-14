package gameforge

import (
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"strconv"
	"strings"
	"sync"
)

type PowChallenge struct {
	Salt   string `json:"salt"`
	Target string `json:"target"`
}

type PowFile struct {
	Pow struct {
		Algorithm  string         `json:"algorithm"`
		Challenges []PowChallenge `json:"challenges"`
	} `json:"pow"`
}

func SolvePow(salt, target string) int64 {
	var nonce int64
	for {
		h := sha256.Sum256([]byte(salt + strconv.FormatInt(nonce, 10)))
		if strings.HasPrefix(hex.EncodeToString(h[:]), target) {
			return nonce
		}
		nonce++
	}
}

func SolvePowParallel(salt, target string, workers int) int64 {
	if workers < 1 {
		workers = 1
	}
	window := int64(2_000_000)
	var start int64
	for {
		found := make(chan int64, workers)
		var wg sync.WaitGroup
		for w := 0; w < workers; w++ {
			wg.Add(1)
			go func(offset int64) {
				defer wg.Done()
				for n := start + offset; n < start+window; n += int64(workers) {
					h := sha256.Sum256([]byte(salt + strconv.FormatInt(n, 10)))
					if strings.HasPrefix(hex.EncodeToString(h[:]), target) {
						found <- n
						return
					}
				}
			}(int64(w))
		}
		wg.Wait()
		close(found)

		best := int64(-1)
		for n := range found {
			if best == -1 || n < best {
				best = n
			}
		}
		if best != -1 {
			return best
		}
		start += window
	}
}

func ParsePowFile(by []byte) (*PowFile, error) {
	var pf PowFile
	if err := json.Unmarshal(by, &pf); err != nil {
		return nil, err
	}
	if pf.Pow.Algorithm != "sha-256" {
		return nil, errors.New("unsupported pow algorithm: " + pf.Pow.Algorithm)
	}
	if len(pf.Pow.Challenges) == 0 {
		return nil, errors.New("pow file has no challenges")
	}
	return &pf, nil
}

func SolvePowFile(by []byte, workers int) ([]int64, error) {
	pf, err := ParsePowFile(by)
	if err != nil {
		return nil, err
	}
	solutions := make([]int64, len(pf.Pow.Challenges))
	for i, c := range pf.Pow.Challenges {
		if workers > 1 {
			solutions[i] = SolvePowParallel(c.Salt, c.Target, workers)
		} else {
			solutions[i] = SolvePow(c.Salt, c.Target)
		}
	}
	return solutions, nil
}
