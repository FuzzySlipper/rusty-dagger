// Rest pose (canonical A-pose, legs straightened) and Standing Idle at t 2, held (#hold 1, never #play): six views.
const out = [];
await browser({op: 'fill', selector: '#hold', value: '1'});
for (const clip of ['rest', 'idle']) {
  await browser({op: 'fill', selector: '#clip', value: clip});
  for (const view of ['front', 'quarter', 'three-quarter', 'side', 'right-side', 'back']) {
    await browser({op: 'fill', selector: '#view', value: view});
    await browser({op: 'fill', selector: '#time', value: '2'});
    await browser({op: 'fill', selector: '#tag', value: `${clip}-${view}`});
    await browser({op: 'click', selector: '#apply'});
    await sleep(700);
    const label = `${clip}-${view}`;
    out.push([label, (await capture({label})).path]);
  }
}
await browser({op: 'fill', selector: '#tag', value: 'end'}); await browser({op: 'click', selector: '#apply'}); await sleep(300);
const page = await browser({op: 'inspect'});
checkpoint('frames', {frames: out, console: (page.console || []).map((m) => m.text).filter((t) => /^(seen|applied) /.test(t))});
