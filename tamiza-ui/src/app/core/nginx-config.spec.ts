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
