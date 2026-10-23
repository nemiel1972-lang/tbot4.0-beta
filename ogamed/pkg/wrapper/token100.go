package wrapper

import (
	"encoding/json"
	"time"
)

const maxToken100Retries = 3

func ajaxError100(body []byte) (newToken string, is100 bool) {
	if len(body) == 0 || len(body) > 4096 || body[0] != '{' {
		return "", false
	}
	var res struct {
		Status string `json:"status"`
		Error  struct {
			Error int `json:"error"`
		} `json:"error"`
		NewAjaxToken string `json:"newAjaxToken"`
	}
	if err := json.Unmarshal(body, &res); err != nil {
		return "", false
	}
	return res.NewAjaxToken, res.Status == "failure" && res.Error.Error == 100
}

func retryOnError100(send func() ([]byte, error), setToken func(string)) ([]byte, error) {
	out, err := send()
	for try := 0; try < maxToken100Retries && err == nil; try++ {
		nt, bad := ajaxError100(out)
		if !bad {
			break
		}
		if nt != "" {
			setToken(nt)
		}
		time.Sleep(time.Duration(400*(try+1)) * time.Millisecond)
		out, err = send()
	}
	return out, err
}
