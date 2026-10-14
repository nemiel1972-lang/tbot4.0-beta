package ogame

import "math"

type Temperature struct {
	Min int64
	Max int64
}

func (t Temperature) Mean() int64 {
	return int64(math.Round(float64(t.Min+t.Max) / 2))
}
