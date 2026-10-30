import nginx from '../../../nginx.conf';
import headers from '../../../nginx-security-headers.conf';

// The UI container's headers and the tile location (design D14, D15). jsdom sends no headers, so the files are read as text.
// The stack check (task 8.2) reads the same headers from the built container.

const csp = /add_header Content-Security-Policy "([^"]*)"/.exec(headers)![1];
const directive = (name: string) => csp.split(';').map((d) => d.trim().split(/\s+/)).find(([key]) => key === name)?.slice(1);

/** The text between the braces of the location whose opening line is `opening`. */
function block(opening: string): string {
  const start = nginx.indexOf(opening);
  expect(start).toBeGreaterThanOrEqual(0);
  return nginx.slice(nginx.indexOf('{', start) + 1, nginx.indexOf('\n    }', start));
}

interface Location {
  /** `=`, `^~`, `~`, `~*` or `''` for a plain prefix. */
  modifier: string;
  path: string;
  /** The text between the braces. */
  body: string;
}

/** The server's locations in file order, with their bodies. Each body ends at the closing brace on its own line. */
function locations(): Location[] {
  const found: Location[] = [];
  for (const match of nginx.matchAll(/^ {4}location\s+(?:(=|\^~|~\*|~)\s+)?(\S+)\s*\{/gm)) {
    const start = match.index! + match[0].length;
    found.push({ modifier: match[1] ?? '', path: match[2], body: nginx.slice(start, nginx.indexOf('\n    }', start)) });
  }
  return found;
}

/**
 * The location nginx picks for a request path, by its own rules: an exact match wins; otherwise the longest prefix, which stops the
 * search when it is `^~`; otherwise the first regular expression in file order that matches; otherwise that longest prefix.
 */
function locationFor(path: string): Location {
  const all = locations();
  const exact = all.find((l) => l.modifier === '=' && l.path === path);
  if (exact) {
    return exact;
  }
  const prefix = all
    .filter((l) => (l.modifier === '' || l.modifier === '^~') && path.startsWith(l.path))
    .sort((a, b) => b.path.length - a.path.length)[0];
  if (prefix?.modifier === '^~') {
    return prefix;
  }
  const regex = all.find((l) => (l.modifier === '~' || l.modifier === '~*') && new RegExp(l.path, l.modifier === '~*' ? 'i' : '').test(path));
  return regex ?? prefix;
}

const directives = (body: string, name: string) => [...body.matchAll(new RegExp(`^\\s*${name}\\s+(.+?);`, 'gm'))].map((m) => m[1]);

describe('nginx headers', () => {
  it('allows images from the page itself, data URLs and the one OpenStreetMap tile origin, and from nowhere else', () => {
    expect(directive('img-src')).toEqual(["'self'", 'data:', 'https://tile.openstreetmap.org']);
    expect(csp).toContain("img-src 'self' data: https://tile.openstreetmap.org;");
  });

  it('keeps every other source narrow: scripts from the page, no eval, no frames, no plugins', () => {
    expect(directive('default-src')).toEqual(["'self'"]);
    expect(directive('script-src')).toEqual(["'self'"]);
    expect(csp).not.toContain('unsafe-eval');
    expect(csp).not.toContain('blob:');
    expect(directive('object-src')).toEqual(["'none'"]);
    expect(directive('frame-ancestors')).toEqual(["'none'"]);
    expect(directive('font-src')).toEqual(["'self'"]);
  });

  it('keeps no-referrer for the whole site (the OpenStreetMap tiles ask for the origin themselves)', () => {
    expect(headers).toContain('add_header Referrer-Policy "no-referrer" always;');
  });
});

describe('nginx map tile location', () => {
  const tiles = () => block('location ^~ /api/v1/basemap-tiles/');

  it('has its own location, a prefix one so the static-asset rule never takes tile paths', () => {
    expect(nginx).toContain('location ^~ /api/v1/basemap-tiles/ {');
    expect(nginx.indexOf('location ^~ /api/v1/basemap-tiles/')).toBeLessThan(nginx.indexOf('location ~* '));
  });

  it('streams without buffering, keeps the links out of the access log and sends the security headers', () => {
    expect(tiles()).toContain('proxy_buffering off;');
    expect(tiles()).toContain('access_log off;');
    expect(tiles()).toContain('include /etc/nginx/snippets/security-headers.conf;');
  });

  it('proxies to the api like the other API locations, and stores nothing', () => {
    expect(tiles()).toContain('proxy_pass $api_upstream;');
    expect(tiles()).toContain('set $api_upstream http://api:8080;');
    expect(tiles()).toContain('proxy_http_version 1.1;');
    expect(tiles()).toContain('proxy_set_header X-Forwarded-Proto $forwarded_proto;');
    expect(tiles()).not.toMatch(/proxy_cache|proxy_store|proxy_temp_path/);
  });

  it('leaves the export link location as it was: buffering off, but logged', () => {
    const exports = block('location ^~ /api/v1/data-exports/');
    expect(exports).toContain('proxy_buffering off;');
    expect(exports).not.toContain('access_log off;');
  });

  it('puts access_log off nowhere but the tile location and the health check', () => {
    expect(nginx.match(/access_log off;/g)).toHaveLength(2);
  });
});

describe('nginx locations for the MCP server', () => {
  const mcp = () => locationFor('/mcp');
  const metadata = () => locationFor('/.well-known/oauth-protected-resource/mcp');
  const assistant = () => locationFor('/api/v1/projects/p1/assistant/completions');

  it('sends /mcp to its own location, an exact one, so the single page app never answers it', () => {
    expect(mcp().modifier).toBe('=');
    expect(mcp().path).toBe('/mcp');
    expect(nginx.indexOf('location = /mcp {')).toBeLessThan(nginx.indexOf('location / {'));
  });

  it('proxies /mcp to the api, like the assistant: same upstream and headers', () => {
    expect(mcp().body).toContain('set $api_upstream http://api:8080;');
    expect(mcp().body).toContain('proxy_pass $api_upstream;');
    expect(mcp().body).toContain('proxy_http_version 1.1;');
    expect(directives(mcp().body, 'proxy_set_header').sort()).toEqual(directives(assistant().body, 'proxy_set_header').sort());
    expect(directives(mcp().body, 'proxy_set_header')).toEqual(
      expect.arrayContaining(['Host $host', 'X-Forwarded-Proto $forwarded_proto', 'X-Forwarded-Host $host']),
    );
  });

  // The API waits at most 120 seconds for a test or a run, and a hosted Claude app gives up at 240, so 300 never cuts a call first.
  it('lets a call wait for five minutes and streams its answer, with a small body limit', () => {
    expect(directives(mcp().body, 'proxy_read_timeout')).toEqual(['300s']);
    expect(directives(mcp().body, 'proxy_buffering')).toEqual(['off']);
    expect(directives(mcp().body, 'client_max_body_size')).toEqual(['1m']);
  });

  it('sends the security headers and adds no CORS headers, anywhere', () => {
    expect(mcp().body).toContain('include /etc/nginx/snippets/security-headers.conf;');
    expect(metadata().body).toContain('include /etc/nginx/snippets/security-headers.conf;');
    expect(directives(mcp().body, 'add_header')).toEqual([]);
    expect(directives(metadata().body, 'add_header')).toEqual([]);
    expect(nginx.toLowerCase()).not.toContain('access-control-');
    expect(headers.toLowerCase()).not.toContain('access-control-');
  });

  it('stores nothing from /mcp and keeps its requests in the access log', () => {
    expect(mcp().body).not.toMatch(/proxy_cache|proxy_store|proxy_temp_path|access_log/);
  });

  it('sends the protected resource metadata, under any resource path, to the api with the default timeout', () => {
    for (const path of ['/.well-known/oauth-protected-resource', '/.well-known/oauth-protected-resource/mcp']) {
      const found = locationFor(path);
      expect(found.modifier).toBe('^~');
      expect(found.path).toBe('/.well-known/oauth-protected-resource');
      expect(found.body).toContain('proxy_pass $api_upstream;');
      expect(found.body).toContain('set $api_upstream http://api:8080;');
    }
    expect(directives(metadata().body, 'proxy_read_timeout')).toEqual([]);
    expect(directives(metadata().body, 'proxy_buffering')).toEqual([]);
    expect(directives(metadata().body, 'proxy_set_header').sort()).toEqual(directives(mcp().body, 'proxy_set_header').sort());
    expect(nginx.indexOf('location ^~ /.well-known/oauth-protected-resource {')).toBeLessThan(nginx.indexOf('location / {'));
  });

  it('keeps the static-asset rule away from the metadata, whatever its path ends with', () => {
    expect(locationFor('/.well-known/oauth-protected-resource/mcp.json').modifier).toBe('^~');
    expect(locationFor('/.well-known/oauth-protected-resource/mcp.json').body).toContain('proxy_pass $api_upstream;');
  });

  it('still serves the app for the pages and the files it always served', () => {
    for (const path of ['/', '/projects', '/projects/p1/overview', '/mcp/', '/mcp-guide', '/.well-known/other']) {
      const found = locationFor(path);
      expect(found.path).toBe('/');
      expect(found.body).toContain('try_files $uri $uri/ /index.html;');
      expect(found.body).not.toContain('proxy_pass');
    }
    expect(locationFor('/index.html')).toMatchObject({ modifier: '=', path: '/index.html' });
    expect(locationFor('/main-ABC123.js').body).toContain('immutable');
    expect(locationFor('/i18n/en.json').path).toBe('/i18n/');
  });

  it('leaves the API and the health check where they were', () => {
    expect(locationFor('/api/v1/system/config').path).toBe('/api/');
    expect(locationFor('/api/v1/projects/p1/assistant/completions').body).toContain('proxy_read_timeout 300s;');
    expect(locationFor('/healthz').path).toBe('/healthz');
  });

  it('proxies nothing but the API paths, /mcp and the metadata to the api', () => {
    const proxied = locations()
      .filter((l) => l.body.includes('proxy_pass'))
      .map((l) => (l.modifier ? `${l.modifier} ${l.path}` : l.path));
    expect(proxied).toEqual([
      '/api/',
      '~ ^/api/v1/projects/[^/]+/data/[^/]+/[^/]+/exports$',
      '~ ^/api/v1/projects/[^/]+/assistant/',
      '^~ /api/v1/data-exports/',
      '^~ /api/v1/basemap-tiles/',
      '= /mcp',
      '^~ /.well-known/oauth-protected-resource',
    ]);
  });
});
