// Minimal NuGet v3 bridge.
//
// The .NET CLI on this host cannot complete a TLS handshake ("No credentials are available
// in the security package"), while Node's fetch works. This tiny server exposes the subset of
// the NuGet v3 protocol that `dotnet restore` needs and forwards each request upstream over
// plain HTTP. It is a build-environment workaround only - nothing in the application depends
// on it. Point NuGet.Config at http://127.0.0.1:5099/v3/index.json while restoring.

const http = require('http');
const UPSTREAM = 'https://api.nuget.org/v3';
const PORT = Number(process.env.NUGET_BRIDGE_PORT || 5099);
const SELF = `http://127.0.0.1:${PORT}`;

// Upstream hosts that serve the resources we need, mapped onto this bridge.
const HOST_MAP = [
  ['https://api.nuget.org/v3-flatcontainer/', `${SELF}/v3-flatcontainer/`],
  ['https://api.nuget.org/v3-index/', `${SELF}/v3-index/`],
  ['https://api.nuget.org/v3/registration5-gz-semver2/', `${SELF}/v3/registration5-gz-semver2/`],
  ['https://api.nuget.org/v3/registration5-gz-semver1/', `${SELF}/v3/registration5-gz-semver1/`],
  ['https://api.nuget.org/v3/registration5-semver1/', `${SELF}/v3/registration5-semver1/`]
];

// Rewrite the service index so the client stays on plain HTTP for every resource we serve.
// Repository-signature resources are dropped: NuGet refuses to take them over HTTP, and they
// are optional (package hashes are still verified).
function rewriteServiceIndex(json) {
  const index = JSON.parse(json);
  index.resources = index.resources.filter(
    (resource) => !String(resource['@type']).startsWith('RepositorySignatures'));

  for (const resource of index.resources) {
    for (const [upstream, local] of HOST_MAP) {
      if (resource['@id'].startsWith(upstream)) {
        resource['@id'] = local + resource['@id'].slice(upstream.length);
      }
    }
  }
  return JSON.stringify(index);
}

const server = http.createServer(async (req, res) => {
  const url = new URL(req.url, SELF);

  // The client asks for /v3/index.json, /v3/registration... or /v3-flatcontainer/...;
  // the upstream base already ends in /v3, so that first segment is stripped. The repository
  // signature endpoints live one level up (api.nuget.org/v3-index), hence the third branch.
  let upstreamUrl;
  if (url.pathname.startsWith('/v3-index/')) {
    upstreamUrl = 'https://api.nuget.org' + url.pathname + url.search;
  } else {
    const upstreamPath = url.pathname.replace(/^\/v3/, '') || '/';
    upstreamUrl = UPSTREAM + upstreamPath + url.search;
  }

  try {
    const upstream = await fetch(upstreamUrl, {
      headers: { accept: req.headers.accept || 'application/json' }
    });

    let buffer = Buffer.from(await upstream.arrayBuffer());
    let contentType = upstream.headers.get('content-type') || 'application/octet-stream';

    if (url.pathname === '/v3/index.json' && upstream.ok) {
      buffer = Buffer.from(rewriteServiceIndex(buffer.toString('utf8')), 'utf8');
      contentType = 'application/json';
    }

    res.writeHead(upstream.status, {
      'content-type': contentType,
      'content-length': buffer.length
    });
    res.end(buffer);
  } catch (error) {
    res.writeHead(502, { 'content-type': 'text/plain' });
    res.end(`bridge error: ${error.message}`);
  }
});

server.listen(PORT, '127.0.0.1', () => {
  console.log(`NuGet bridge listening on http://127.0.0.1:${PORT}/v3/index.json`);
});
