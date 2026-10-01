// 旁白字幕（无配音版也能看懂）
SUBS.forEach(([a, b, text]) => {
  const el = add(root, `<div class="sub">${text}</div>`);
  A(el, a, .25, [{ opacity: 0 }, { opacity: 1 }]);
  A(el, b - .2, .2, [{ opacity: 1 }, { opacity: 0 }]);
});
window.seek = t => { document.getAnimations().forEach(a => { a.pause(); a.currentTime = t * 1000; }); };
window.READY = true;
