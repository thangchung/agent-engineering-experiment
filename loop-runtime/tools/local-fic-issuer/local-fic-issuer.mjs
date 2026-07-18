#!/usr/bin/env node
import { createHash, createPrivateKey, createPublicKey, randomUUID, sign, generateKeyPairSync } from 'node:crypto';
import { createServer } from 'node:http';
import { mkdirSync, readFileSync, writeFileSync, existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const stateDir = join(__dirname, '.state');
const keyPath = join(stateDir, 'fic-dev-private.pem');
const assertionPath = join(stateDir, 'assertion.jwt');
const subject = process.env.FIC_SUBJECT ?? 'loop-runtime-local';
const audience = process.env.FIC_AUDIENCE ?? 'api://AzureADTokenExchange';
const port = Number.parseInt(process.env.PORT ?? '8787', 10);

mkdirSync(stateDir, { recursive: true });

if (!existsSync(keyPath)) {
  const { privateKey } = generateKeyPairSync('rsa', { modulusLength: 2048 });
  writeFileSync(keyPath, privateKey.export({ type: 'pkcs8', format: 'pem' }));
}

const privateKeyPem = readFileSync(keyPath, 'utf8');
const privateKey = createPrivateKey(privateKeyPem);
const publicKey = createPublicKey(privateKey);
const publicJwk = publicKey.export({ format: 'jwk' });
const kid = createHash('sha256')
  .update(`${publicJwk.n}.${publicJwk.e}`)
  .digest('base64url')
  .slice(0, 32);

const jwks = {
  keys: [
    {
      ...publicJwk,
      kid,
      use: 'sig',
      alg: 'RS256',
    },
  ],
};

function json(response, statusCode, body) {
  response.writeHead(statusCode, { 'content-type': 'application/json' });
  response.end(JSON.stringify(body, null, 2));
}

function b64urlJson(value) {
  return Buffer.from(JSON.stringify(value)).toString('base64url');
}

function mintAssertion(issuer) {
  const now = Math.floor(Date.now() / 1000);
  const header = b64urlJson({ alg: 'RS256', typ: 'JWT', kid });
  const payload = b64urlJson({
    iss: issuer,
    sub: subject,
    aud: audience,
    jti: randomUUID(),
    nbf: now - 60,
    iat: now,
    exp: now + 50 * 60,
  });
  const input = `${header}.${payload}`;
  const signature = sign('RSA-SHA256', Buffer.from(input), privateKey).toString('base64url');
  return `${input}.${signature}`;
}

const server = createServer((request, response) => {
  const host = request.headers['x-forwarded-host'] ?? request.headers.host;
  const proto = request.headers['x-forwarded-proto'] ?? 'http';
  const issuer = `${proto}://${host}`;

  if (request.url === '/.well-known/openid-configuration') {
    json(response, 200, {
      issuer,
      jwks_uri: `${issuer}/jwks.json`,
      id_token_signing_alg_values_supported: ['RS256'],
      response_types_supported: ['id_token'],
      subject_types_supported: ['public'],
    });
    return;
  }

  if (request.url === '/jwks.json') {
    json(response, 200, jwks);
    return;
  }

  if (request.url === '/mint') {
    const jwt = mintAssertion(issuer);
    writeFileSync(assertionPath, jwt);
    json(response, 200, {
      issuer,
      subject,
      audience,
      assertionPath,
      expiresInSeconds: 3000,
      tokenPreview: `${jwt.slice(0, 30)}...${jwt.slice(-20)}`,
    });
    return;
  }

  json(response, 200, {
    issuer,
    subject,
    audience,
    discovery: `${issuer}/.well-known/openid-configuration`,
    jwks: `${issuer}/jwks.json`,
    mint: `${issuer}/mint`,
    assertionPath,
  });
});

server.listen(port, () => {
  console.log(`Local FIC issuer listening on http://localhost:${port}`);
  console.log(`Subject: ${subject}`);
  console.log(`Audience: ${audience}`);
  console.log(`Assertion file: ${assertionPath}`);
});