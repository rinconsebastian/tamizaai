// Python files are imported as text (the "loader" option of the build in angular.json): the script templates.
declare module '*.py' {
  const source: string;
  export default source;
}
