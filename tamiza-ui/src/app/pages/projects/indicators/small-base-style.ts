// How a small base is drawn (add-small-sample-warnings, design D10): a lighter mark, with a dashed outline in the mark's own color. SVG
// attributes, never utilities, so the initial bundle does not grow. Kept apart, with no import, so the image composer (a lazy chunk of its
// own) can read the opacity without pulling the base figures' modules with it.

export const smallBaseOpacity = 0.35;
export const smallBaseDash = '3 2';
export const smallBaseStroke = 1.5;
