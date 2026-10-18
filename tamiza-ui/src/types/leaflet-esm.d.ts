// Leaflet's ES module build (design D1): the package's `main` is the UMD file, which esbuild would not split into a lazy chunk.
// Only `indicators/map-engine.ts` imports it.
declare module 'leaflet/dist/leaflet-src.esm.js' {
  export * from 'leaflet';
}
