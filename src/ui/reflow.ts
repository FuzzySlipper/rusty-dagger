/**
 * Classic text is broken into lines by hand at the width of the donor's windows, so drawn as it comes it fills half
 * a wider page. Reflowing joins the lines of each paragraph and lets the page wrap them. A paragraph ends at a blank
 * line, or where the source broke a line early: one that ends a sentence well short of the paragraph's longest
 * line, or a short heading ("Page 1") before a line that opens with a capital.
 */
export function reflow(text: string): string {
  const paragraphs: string[] = [];
  for (const block of text.split(/\n[ \t]*\n/)) {
    const lines = block.split('\n').map(line => line.trim()).filter(line => line !== '');
    if (lines.length === 0) continue;
    const longest = Math.max(...lines.map(line => line.length));
    let current: string[] = [];
    lines.forEach((line, index) => {
      current.push(line);
      const next = lines[index + 1];
      const endsSentence = /[.!?:"'”)]$/.test(line) && line.length < longest * .75;
      const heading = line.length < longest * .4 && next !== undefined && /^[A-Z0-9"'“(]/.test(next);
      if (next === undefined || endsSentence || heading) { paragraphs.push(current.join(' ')); current = []; }
    });
  }
  return paragraphs.join('\n\n');
}
