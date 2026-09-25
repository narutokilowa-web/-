# Extra CA certificates for builds (optional)

Networks with a TLS-inspecting proxy (common in corporate and government networks) break `dotnet restore` and `npm ci` inside Docker builds.
Put the proxy's CA certificate here as `*.crt` (PEM). The Dockerfiles install every `*.crt` in this folder into the build image's trust store.

`*.crt` files are git-ignored. **Never commit certificates or keys.**
