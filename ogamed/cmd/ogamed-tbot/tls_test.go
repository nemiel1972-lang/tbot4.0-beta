package main

import (
	"context"
	"crypto/ecdsa"
	"crypto/elliptic"
	"crypto/rand"
	"crypto/tls"
	"crypto/x509"
	"crypto/x509/pkix"
	"encoding/pem"
	"math/big"
	"net"
	"net/http"
	"os"
	"path/filepath"
	"strconv"
	"testing"
	"time"

	"github.com/labstack/echo/v4"
	"github.com/urfave/cli/v3"
)

func runTLSFlags(t *testing.T, args ...string) (enable bool, key, cert string) {
	t.Helper()
	cmd := &cli.Command{
		Name:  "ogamed-test",
		Flags: tlsFlags(),
		Action: func(_ context.Context, c *cli.Command) error {
			enable = c.Bool("enable-tls")
			key = c.String("tls-key-file")
			cert = c.String("tls-cert-file")
			return nil
		},
	}
	if err := cmd.Run(context.Background(), append([]string{"ogamed-test"}, args...)); err != nil {
		t.Fatalf("Flags konnten nicht gelesen werden: %v", err)
	}
	return
}

func TestTLSFlagsStandardwerte(t *testing.T) {
	enable, key, cert := runTLSFlags(t)
	if enable {
		t.Error("TLS darf ohne Flag nicht aktiv sein")
	}
	if key != "~/.ogame/key.pem" || cert != "~/.ogame/cert.pem" {
		t.Errorf("unerwartete Standardpfade: key=%q cert=%q", key, cert)
	}
}

func TestTLSFlagsUeberKommandozeile(t *testing.T) {
	enable, key, cert := runTLSFlags(t, "--enable-tls", "--tls-key-file", "/k.pem", "--tls-cert-file", "/c.pem")
	if !enable {
		t.Error("--enable-tls wurde nicht als true gelesen")
	}
	if key != "/k.pem" || cert != "/c.pem" {
		t.Errorf("key=%q cert=%q", key, cert)
	}
}

func TestTLSFlagsUeberUmgebung(t *testing.T) {
	t.Setenv("OGAMED_ENABLE_TLS", "true")
	t.Setenv("OGAMED_TLS_KEYFILE", "/aus-env/key.pem")
	t.Setenv("OGAMED_TLS_CERTFILE", "/aus-env/cert.pem")
	enable, key, cert := runTLSFlags(t)
	if !enable {
		t.Error("OGAMED_ENABLE_TLS=true wurde nicht als true gelesen")
	}
	if key != "/aus-env/key.pem" {
		t.Errorf("tls-key-file las %q statt des KEYFILE-Wertes", key)
	}
	if cert != "/aus-env/cert.pem" {
		t.Errorf("tls-cert-file las %q statt des CERTFILE-Wertes", cert)
	}
}

func TestExpandHome(t *testing.T) {
	home := t.TempDir()
	t.Setenv("HOME", home)
	t.Setenv("USERPROFILE", home)
	if got := expandHome("~/.ogame/key.pem"); got != filepath.Join(home, ".ogame", "key.pem") {
		t.Errorf("expandHome(~/...) = %q", got)
	}
	if got := expandHome("~"); got != home {
		t.Errorf("expandHome(~) = %q", got)
	}
	for _, p := range []string{"/abs/key.pem", "rel/key.pem", "~andere/key.pem", ""} {
		if got := expandHome(p); got != p {
			t.Errorf("expandHome(%q) = %q, erwartet unveraendert", p, got)
		}
	}
}

func schreibeSelbstsigniertesZertifikat(t *testing.T, dir string) (certFile, keyFile string) {
	t.Helper()
	priv, err := ecdsa.GenerateKey(elliptic.P256(), rand.Reader)
	if err != nil {
		t.Fatal(err)
	}
	tmpl := &x509.Certificate{
		SerialNumber: big.NewInt(1),
		Subject:      pkix.Name{CommonName: "127.0.0.1"},
		NotBefore:    time.Now().Add(-time.Hour),
		NotAfter:     time.Now().Add(time.Hour),
		KeyUsage:     x509.KeyUsageDigitalSignature,
		ExtKeyUsage:  []x509.ExtKeyUsage{x509.ExtKeyUsageServerAuth},
		IPAddresses:  []net.IP{net.ParseIP("127.0.0.1")},
	}
	der, err := x509.CreateCertificate(rand.Reader, tmpl, tmpl, &priv.PublicKey, priv)
	if err != nil {
		t.Fatal(err)
	}
	keyDER, err := x509.MarshalECPrivateKey(priv)
	if err != nil {
		t.Fatal(err)
	}
	certFile = filepath.Join(dir, "cert.pem")
	keyFile = filepath.Join(dir, "key.pem")
	if err := os.WriteFile(certFile, pem.EncodeToMemory(&pem.Block{Type: "CERTIFICATE", Bytes: der}), 0o600); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(keyFile, pem.EncodeToMemory(&pem.Block{Type: "EC PRIVATE KEY", Bytes: keyDER}), 0o600); err != nil {
		t.Fatal(err)
	}
	return
}

func TestTLSServerStartetMitUmgebungsVariablen(t *testing.T) {
	home := t.TempDir()
	t.Setenv("HOME", home)
	t.Setenv("USERPROFILE", home)
	ogameDir := filepath.Join(home, ".ogame")
	if err := os.MkdirAll(ogameDir, 0o700); err != nil {
		t.Fatal(err)
	}
	schreibeSelbstsigniertesZertifikat(t, ogameDir)

	t.Setenv("OGAMED_ENABLE_TLS", "true")
	enable, key, cert := runTLSFlags(t)
	if !enable {
		t.Fatal("TLS nicht aktiviert")
	}

	l, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	port := l.Addr().(*net.TCPAddr).Port
	l.Close()

	e := echo.New()
	e.HideBanner = true
	e.HidePort = true
	e.GET("/ping", func(c echo.Context) error { return c.String(http.StatusOK, "pong") })
	done := make(chan error, 1)
	go func() { done <- e.StartTLS("127.0.0.1:"+strconv.Itoa(port), expandHome(cert), expandHome(key)) }()
	defer func() {
		ctx, cancel := context.WithTimeout(context.Background(), 2*time.Second)
		defer cancel()
		_ = e.Shutdown(ctx)
	}()

	client := &http.Client{
		Timeout:   2 * time.Second,
		Transport: &http.Transport{TLSClientConfig: &tls.Config{InsecureSkipVerify: true}},
	}
	var lastErr error
	for i := 0; i < 30; i++ {
		select {
		case err := <-done:
			t.Fatalf("Server vorzeitig beendet: %v", err)
		default:
		}
		resp, err := client.Get("https://127.0.0.1:" + strconv.Itoa(port) + "/ping")
		if err == nil {
			resp.Body.Close()
			if resp.StatusCode != http.StatusOK {
				t.Fatalf("Status %d", resp.StatusCode)
			}
			return
		}
		lastErr = err
		time.Sleep(100 * time.Millisecond)
	}
	t.Fatalf("https-Abfrage nie erfolgreich: %v", lastErr)
}
