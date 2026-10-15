import type { ScriptKind } from '../../../core/projects/pipeline.service';
import columnsForTheDashboard from './output/columns-for-the-dashboard.py';
import countByGroup from './indicator/count-by-group.py';
import dropTestSubmissions from './clean/drop-test-submissions.py';
import fixTypesAndRecode from './clean/fix-types-and-recode.py';
import joinRepeatToMain from './output/join-repeat-to-main.py';
import monthlySeries from './indicator/monthly-series.py';
import rateByGroup from './indicator/rate-by-group.py';

/**
 * A starting point for a script. Its name and description are in the translations, under `scripts.templates.<kind>.<id>`.
 * Each is written in `# %%` cells with `ctx.show`, names the columns to adapt in constants at the top, and uses only
 * columns every form has, so it runs unchanged on any project.
 */
export interface ScriptTemplate {
  id: string;
  source: string;
}

export const scriptTemplates: Record<ScriptKind, ScriptTemplate[]> = {
  clean: [
    { id: 'drop-test-submissions', source: dropTestSubmissions },
    { id: 'fix-types-and-recode', source: fixTypesAndRecode },
  ],
  output: [
    { id: 'columns-for-the-dashboard', source: columnsForTheDashboard },
    { id: 'join-repeat-to-main', source: joinRepeatToMain },
  ],
  indicator: [
    { id: 'count-by-group', source: countByGroup },
    { id: 'rate-by-group', source: rateByGroup },
    { id: 'monthly-series', source: monthlySeries },
  ],
};

/** What a new indicator starts from. */
export const newIndicatorTemplate = scriptTemplates.indicator[0];
