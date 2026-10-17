package utils

import (
	"cmp"
	"github.com/PuerkitoBio/goquery"
	"iter"
	"math"
	"math/rand"
	"regexp"
	"strconv"
	"strings"
	"time"
)

func ParseInt(val string) int64 {
	val = strings.Replace(val, ".", "", -1)
	val = strings.Replace(val, ",", "", -1)
	val = strings.TrimSpace(val)
	return DoParseI64(val)
}

func Group(m []string, i int) string {
	if i < 0 || i >= len(m) {
		return ""
	}
	return m[i]
}

func GroupB(m [][]byte, i int) []byte {
	if i < 0 || i >= len(m) {
		return nil
	}
	return m[i]
}

func ToInt(buf []byte) (n int) {
	for _, v := range buf {
		n = n*10 + int(v-'0')
	}
	return
}

func I64Ptr(v int64) *int64 {
	return &v
}

func Noop(_ ...any) {}

func Clamp[T cmp.Ordered](val, pMin, pMax T) T {
	val = min(val, pMax)
	val = max(val, pMin)
	return val
}

func ParseI64(v string) (out int64, err error) {
	return strconv.ParseInt(v, 10, 64)
}

func DoParseI64(v string) (out int64) {
	out, _ = ParseI64(v)
	return
}

type Ints interface {
	~int64 | ~int
}

func FI64[T Ints](v T) string {
	return strconv.FormatInt(int64(v), 10)
}

func DoCastF64(v any) float64 {
	if f, ok := v.(float64); ok {
		return f
	}
	return 0
}

func DoCastStr(v any) string {
	if str, ok := v.(string); ok {
		return str
	}
	return ""
}

func GetNbr(doc *goquery.Document, name string) int64 {
	div := doc.Find("div." + name)
	level := div.Find("span.level")
	level.Children().Remove()
	return ParseInt(level.Text())
}

func GetNbrShips(doc *goquery.Document, name string) int64 {
	div := doc.Find("div." + name)
	title := div.AttrOr("title", "")
	if title == "" {
		title = div.Find("a").AttrOr("title", "")
	}
	m := regexp.MustCompile(`.+\(([\d.,]+)\)`).FindStringSubmatch(title)
	if len(m) != 2 {
		return 0
	}
	return ParseInt(m[1])
}

type Equalable[T any] interface {
	Equal(other T) bool
}

func InArray[T Equalable[T]](needle T, haystack []T) bool {
	for _, el := range haystack {
		if needle.Equal(el) {
			return true
		}
	}
	return false
}

func InArr[T comparable](needle T, haystack []T) bool {
	for _, el := range haystack {
		if needle == el {
			return true
		}
	}
	return false
}

func RandChoice[T any](arr []T) T {
	if len(arr) == 0 {
		panic("empty array")
	}
	return arr[rand.Intn(len(arr))]
}

func Random(min, max int64) int64 {
	if min == max {
		return min
	}
	if max < min {
		min, max = max, min
	}
	return rand.Int63n(max-min+1) + min
}

func RandDuration(min, max time.Duration) time.Duration {
	n := Random(min.Nanoseconds(), max.Nanoseconds())
	return time.Duration(n) * time.Nanosecond
}

func randDur(min, max int64, dur time.Duration) time.Duration {
	return RandDuration(time.Duration(min)*dur, time.Duration(max)*dur)
}

func RandMs(min, max int64) time.Duration {
	return randDur(min, max, time.Millisecond)
}

func RandFloat(min, max float64) float64 {
	if min == max {
		return min
	}
	if max < min {
		min, max = max, min
	}
	return rand.Float64()*(max-min) + min
}

func Count2[K, V any](it iter.Seq2[K, V]) (out int) {
	for range it {
		out++
	}
	return
}

func Any2[K, V any](it iter.Seq2[K, V], clb func(K, V) bool) bool {
	for k, v := range it {
		if clb(k, v) {
			return true
		}
	}
	return false
}

func All[V any](it iter.Seq[V], clb func(V) bool) bool {
	for v := range it {
		if !clb(v) {
			return false
		}
	}
	return true
}

func Ternary[T any](predicate bool, a, b T) T {
	if predicate {
		return a
	}
	return b
}

func TernaryOrZero[T any](predicate bool, a T) T {
	var zero T
	return Ternary(predicate, a, zero)
}

func Or[T comparable](a, b T) (zero T) {
	return Ternary(a != zero, a, b)
}

func RoundThousandth(n float64) float64 {
	return math.Floor(n*1000) / 1000
}

func Ptr[T any](v T) *T { return &v }

func First[T any](a T, _ ...any) T { return a }

func Second[T any](_ any, a T, _ ...any) T { return a }

func Find[T any](arr []T, predicate func(T) bool) (out *T) {
	return First(FindIdx(arr, predicate))
}

func FindIdx[T any](arr []T, predicate func(T) bool) (*T, int) {
	for i, el := range arr {
		if predicate(el) {
			return &el, i
		}
	}
	return nil, -1
}

func Deref[T any](v *T) T {
	var zero T
	if v == nil {
		return zero
	}
	return *v
}

func Default[T any](v *T, d T) T {
	if v == nil {
		return d
	}
	return *v
}
