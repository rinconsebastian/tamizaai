import { QueryRow, WidgetTooltipItem } from '../../../core/projects/indicators.service';
import { DataColumnType } from '../../../core/projects/data.service';
import { plainIndicator, valueFormatter } from './format';
import { TooltipContext, tooltipContent, tooltipLabel, tooltipSpeech, tooltipValue, tooltipValues } from './map-tooltip';

const labels: Record<string, string> = {
  'indicators.chart.noGroup': '(none)',
  'indicators.map.yes': 'Yes',
  'indicators.map.no': 'No',
};
const t = (key: string, params?: Record<string, unknown>) => (key === 'indicators.map.combines' ? `Combines ${params?.['count']} rows` : (labels[key] ?? key));

const item = (column: string, label: string | null = null, format: WidgetTooltipItem['format'] = null): WidgetTooltipItem => ({ column, label, format });

function context(items: WidgetTooltipItem[], types: DataColumnType[] | undefined, indicator: Parameters<typeof valueFormatter>[0] = plainIndicator, label = 'Survey progress'): TooltipContext {
  return { items, types, label, format: valueFormatter(indicator, 'en-US'), locale: 'en-US', t };
}

const block = (overrides: Partial<QueryRow> = {}): QueryRow => ({ group: '0042', value: 0.45, geometry: null, rows: 1, tooltip: ['Cra 5 # 10-20', 20, 9], ...overrides });

describe('map tooltip', () => {
  const progress = { format: 'percent' as const, unit: null };
  const items = [item('address', 'Dirección'), item('sample_target', 'Meta muestral'), item('achieved', 'Encuestas logradas')];
  const types: DataColumnType[] = ['text', 'integer', 'integer'];

  describe('content', () => {
    it('has the group as its heading, then the plotted value, then the tooltip columns in order', () => {
      const content = tooltipContent(block(), context(items, types, progress));

      expect(content.heading).toBe('0042');
      expect(content.lines.map((line) => `${line.label}: ${line.value}`)).toEqual([
        'Survey progress: 45%',
        'Dirección: Cra 5 # 10-20',
        'Meta muestral: 20',
        'Encuestas logradas: 9',
      ]);
      expect(content.footer).toBeNull();
    });

    it('writes the group of a row with none as (none)', () => {
      expect(tooltipContent(block({ group: null }), context([], undefined)).heading).toBe('(none)');
    });

    it('writes the plotted value with the indicator\'s format and unit', () => {
      const content = tooltipContent(block({ value: 12450, tooltip: [] }), context([], undefined, { format: 'count', unit: 'households' }, 'Households'));

      expect(content.lines).toEqual([{ label: 'Households', value: '12,450 households', align: 'end' }]);
    });

    it('does not apply the indicator\'s percent format to a tooltip column (20 stays 20, not 2,000%)', () => {
      const content = tooltipContent(block({ tooltip: [20] }), context([item('sample_target')], ['integer'], progress));

      expect(content.lines[1].value).toBe('20');
    });

    it('labels an item with its column\'s name when it has no label, and trims the label', () => {
      expect(tooltipLabel(item('sample_target'))).toBe('sample_target');
      expect(tooltipLabel(item('sample_target', '   '))).toBe('sample_target');
      expect(tooltipLabel(item('address', '  Dirección '))).toBe('Dirección');
    });

    it('says how many rows a group combines, only when it combines more than one', () => {
      expect(tooltipContent(block({ rows: 2 }), context(items, types)).footer).toBe('Combines 2 rows');
      expect(tooltipContent(block({ rows: 1 }), context(items, types)).footer).toBeNull();
      expect(tooltipContent(block({ rows: undefined }), context(items, types)).footer).toBeNull();
    });

    it('lines numbers up at the end and text at the start', () => {
      const content = tooltipContent(block(), context(items, types, progress));

      expect(content.lines.map((line) => line.align)).toEqual(['end', 'start', 'end', 'end']);
    });

    it('reads a tooltip as one text for a screen reader', () => {
      const content = tooltipContent(block({ rows: 2 }), context(items, types, progress));

      expect(tooltipSpeech(content)).toBe('0042. Survey progress: 45%. Dirección: Cra 5 # 10-20. Meta muestral: 20. Encuestas logradas: 9. Combines 2 rows');
    });

    it('keeps markup as text, in a value and in a label', () => {
      const hostile = '<img src=x onerror=alert(1)>';

      const content = tooltipContent(block({ tooltip: [hostile] }), context([item('address', hostile)], ['text']));

      expect(content.lines[1]).toEqual({ label: hostile, value: hostile, align: 'start' });
    });
  });

  describe('formats', () => {
    const number = (value: number, type: DataColumnType, format: WidgetTooltipItem['format'] = null) => tooltipValue(value, type, item('c', null, format), 'en-US', t);

    it('shows progress as a percentage when the column has that format', () => {
      expect(number(0.45, 'decimal', 'percent')).toBe('45%');
    });

    it('applies count and decimal to numbers', () => {
      expect(number(12450, 'integer', 'count')).toBe('12,450');
      expect(number(3.14159, 'decimal', 'decimal')).toBe('3.14');
      expect(number(3, 'decimal', 'decimal')).toBe('3.00');
    });

    it('shows an integer with grouped digits and a decimal with up to two decimals when no format is chosen', () => {
      expect(number(12450, 'integer')).toBe('12,450');
      expect(number(3.14159, 'decimal')).toBe('3.14');
      expect(number(3, 'decimal')).toBe('3');
      expect(number(0.5, 'decimal')).toBe('0.5');
    });

    it('ignores a format on a column that is no longer a number', () => {
      const text = tooltipValue('about half', 'text', item('progress', null, 'percent'), 'en-US', t);
      const date = tooltipValue('2026-09-01', 'date', item('progress', null, 'count'), 'en-US', t);
      const bool = tooltipValue(true, 'boolean', item('progress', null, 'percent'), 'en-US', t);

      expect(text).toBe('about half');
      expect(date).toBe('Sep 1, 2026');
      expect(bool).toBe('Yes');
    });

    it('writes true and false as Yes and No', () => {
      expect(tooltipValue(true, 'boolean', item('c'), 'en-US', t)).toBe('Yes');
      expect(tooltipValue(false, 'boolean', item('c'), 'en-US', t)).toBe('No');
    });

    it('writes a date in the medium date format', () => {
      expect(tooltipValue('2026-09-01', 'date', item('c'), 'en-US', t)).toBe('Sep 1, 2026');
    });

    it('writes a timestamp to the minute in the shared format, in the offset it carries', () => {
      expect(tooltipValue('2026-09-01T21:30:05-05:00', 'timestamp', item('c'), 'en-US', t)).toBe('Sep 1, 2026, 9:30 PM');
    });

    it('writes no value as a dash', () => {
      expect(tooltipValue(null, 'text', item('c'), 'en-US', t)).toBe('—');
      expect(tooltipValue(undefined, 'integer', item('c', null, 'count'), 'en-US', t)).toBe('—');
    });

    it('writes the text as it is', () => {
      expect(tooltipValue('Cra 5 # 10-20', 'text', item('c'), 'en-US', t)).toBe('Cra 5 # 10-20');
    });

    it('writes a missing value of every row as a dash in the tooltip and the table alike', () => {
      const row = block({ tooltip: [null, null, null] });

      expect(tooltipValues(row, context(items, types))).toEqual(['—', '—', '—']);
      expect(tooltipContent(row, context(items, types)).lines.slice(1).map((line) => line.value)).toEqual(['—', '—', '—']);
    });

    it('keeps the order of the items, however the row lists its values', () => {
      const reordered = [item('achieved', 'Encuestas logradas'), item('address', 'Dirección')];

      const content = tooltipContent(block({ tooltip: [9, 'Cra 5 # 10-20'] }), context(reordered, ['integer', 'text']));

      expect(content.lines.slice(1).map((line) => line.label)).toEqual(['Encuestas logradas', 'Dirección']);
      expect(content.lines.slice(1).map((line) => line.value)).toEqual(['9', 'Cra 5 # 10-20']);
    });

    it('reads values by their own type when an answer carries no types', () => {
      expect(tooltipValue(20, undefined, item('c'), 'en-US', t)).toBe('20');
      expect(tooltipValue(0.45, undefined, item('c', null, 'percent'), 'en-US', t)).toBe('45%');
    });
  });
});
