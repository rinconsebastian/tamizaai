import { TestBed } from '@angular/core/testing';
import { Downloader } from './download.service';

describe('Downloader.save', () => {
  let created: Blob[];
  let revoked: string[];
  let clicked: { href: string; download: string; connected: boolean }[];

  beforeEach(() => {
    vi.useFakeTimers();
    created = [];
    revoked = [];
    clicked = [];
    vi.spyOn(URL, 'createObjectURL').mockImplementation((blob) => {
      created.push(blob as Blob);
      return `blob:tamiza/${created.length}`;
    });
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation((url) => void revoked.push(url));
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      clicked.push({ href: this.href, download: this.download, connected: this.isConnected });
    });
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('clicks a temporary link to the file under the given name and removes the link', () => {
    const blob = new Blob(['<svg/>'], { type: 'image/svg+xml;charset=utf-8' });

    TestBed.inject(Downloader).save(blob, 'census-2026_housing_coverage_2026-11-02.svg');

    expect(created).toEqual([blob]);
    expect(clicked).toEqual([{ href: 'blob:tamiza/1', download: 'census-2026_housing_coverage_2026-11-02.svg', connected: true }]);
    expect(document.querySelectorAll('a[download]').length).toBe(0);
  });

  it('releases the file 30 seconds later, and not before', () => {
    TestBed.inject(Downloader).save(new Blob(['x']), 'a.png');

    vi.advanceTimersByTime(29_999);
    expect(revoked).toEqual([]);
    vi.advanceTimersByTime(1);
    expect(revoked).toEqual(['blob:tamiza/1']);
  });

  it('releases each file on its own', () => {
    const downloader = TestBed.inject(Downloader);
    downloader.save(new Blob(['1']), 'a.png');
    vi.advanceTimersByTime(10_000);
    downloader.save(new Blob(['2']), 'b.png');

    vi.advanceTimersByTime(20_000);
    expect(revoked).toEqual(['blob:tamiza/1']);
    vi.advanceTimersByTime(10_000);
    expect(revoked).toEqual(['blob:tamiza/1', 'blob:tamiza/2']);
  });
});
