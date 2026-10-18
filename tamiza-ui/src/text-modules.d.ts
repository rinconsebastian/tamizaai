// Stylesheets and nginx files that a few specs read as text (the "loader" option of the build in angular.json), to pin selectors
// and headers that no jsdom paints or sends.
declare module '*.css' {
  const source: string;
  export default source;
}

declare module '*.conf' {
  const source: string;
  export default source;
}
