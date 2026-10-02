"use strict";
(() => {
  const $ = selector => document.querySelector(selector);
  const app = $("#app"), results = $("#results"), drawer = $("#drawer");
  const games = [
    { id: 0, name: "雾海回声", title: "ECHOES\nOF THE MIST", style: "mist", category: "冒险", favorite: true, recent: true },
    { id: 1, name: "深空航线", title: "BEYOND\nTHE ORBIT", style: "space", category: "探索", favorite: false, recent: true },
    { id: 2, name: "赤沙旅人", title: "RED SAND\nVOYAGER", style: "sand", category: "角色扮演", favorite: false, recent: false },
    { id: 3, name: "夜城漫游", title: "AFTER\nMIDNIGHT", style: "night", category: "冒险", favorite: true, recent: true },
    { id: 4, name: "森间来信", title: "LETTERS\nFROM THE WOODS", style: "forest", category: "探索", favorite: false, recent: false },
    { id: 5, name: "极地信标", title: "POLAR\nSIGNAL", style: "polar", category: "冒险", favorite: false, recent: false }
  ];
  let navigation = "all", listMode = false, selected = null, systemReduced = false, foreground = document.hasFocus();
  let introTimeline, drawerTimeline, demoTimeline, ambientTween;
  const media = gsap.matchMedia();
  const reduced = () => systemReduced || $("#reduce").checked;
  const duration = seconds => reduced() ? 0 : seconds * Number($("#speed").value);
  const animate = (target, vars) => gsap.to(target, { ease: "power2.out", overwrite: "auto", ...vars, duration: duration(vars.duration ?? .16) });
  function startAmbient() {
    ambientTween?.kill(); ambientTween = null;
    if (reduced() || document.hidden || !foreground) return;
    ambientTween = gsap.fromTo($(".flow-line i"), { xPercent: -90 }, { xPercent: 90, duration: 3.8, repeat: -1, ease: "none" });
    ambientTween.data = "ambient";
  }
  const poster = game => `<div class="poster ${game.style}"><i class="orbit"></i><i class="moon"></i><i class="ridge"></i><i class="ridge second"></i><span class="poster-code">CX COLLECTION / 0${game.id + 1}</span><span class="poster-title">${game.title}</span><span class="poster-subtitle">A WORLD OF YOUR OWN</span></div>`;
  function stopDemo() { demoTimeline?.kill(); demoTimeline = null; $("#demo").textContent = "▷ 自动演示"; }
  function resetMotion() {
    introTimeline?.kill(); drawerTimeline?.kill(); stopDemo();
    gsap.killTweensOf(app.querySelectorAll("*"));
    gsap.set(app.querySelectorAll("*"), { clearProps: "transform,opacity,visibility" });
    window.cxLiquid.setEnabled(!reduced());
    startAmbient();
  }
  media.add({ all: "(min-width: 0px)", reduceMotion: "(prefers-reduced-motion: reduce)" }, context => {
    systemReduced = context.conditions.reduceMotion;
    resetMotion();
    return resetMotion;
  });
  function reveal(target, seconds = .16, y = 6) {
    gsap.killTweensOf(target);
    if (reduced()) { gsap.set(target, { clearProps: "transform,opacity,visibility" }); return; }
    return gsap.fromTo(target, { y, autoAlpha: .72 }, { y: 0, autoAlpha: 1, duration: duration(seconds), ease: "power2.out", overwrite: true, clearProps: "transform,opacity,visibility" });
  }
  function enter() {
    introTimeline?.kill();
    const targets = [$(".page-header"), $(".toolbar"), results];
    gsap.killTweensOf(targets);
    if (reduced()) { gsap.set(targets, { clearProps: "transform,opacity,visibility" }); return; }
    introTimeline = gsap.timeline({ defaults: { duration: duration(.16), ease: "power2.out", clearProps: "transform,opacity,visibility" } })
      .addLabel("enter", 0)
      .fromTo(targets[0], { y: 6, autoAlpha: .72 }, { y: 0, autoAlpha: 1 }, "enter")
      .fromTo(targets[1], { y: 6, autoAlpha: .72 }, { y: 0, autoAlpha: 1 }, duration(.025))
      .fromTo(targets[2], { y: 6, autoAlpha: .72 }, { y: 0, autoAlpha: 1 }, duration(.05));
  }
  function notice(message) { $("#notice").textContent = message; reveal($("#notice"), .14, 0); }
  function render() {
    gsap.killTweensOf(results.querySelectorAll("*"));
    const term = $("#search").value.trim().toLocaleLowerCase();
    const category = $("#category").value;
    let visible = games.filter(game => (!term || game.name.toLocaleLowerCase().includes(term)) && (category === "all" || game.category === category) && (navigation === "all" || (navigation === "favorite" ? game.favorite : game.recent)));
    if ($("#sort").value === "recent") visible = visible.toSorted((a, b) => Number(b.recent) - Number(a.recent));
    $("#result-count").textContent = `${visible.length} 个游戏`;
    $("#favorites-count").textContent = games.filter(game => game.favorite).length;
    results.classList.toggle("list-mode", listMode);
    $("#stat-favorites").textContent = games.filter(game => game.favorite).length;
    results.innerHTML = visible.length ? visible.map(game => `<article class="card${selected === game.id ? " is-selected" : ""}" data-id="${game.id}"><i class="card-sheen" aria-hidden="true"></i><button class="cover-button" data-action="detail" aria-label="查看 ${game.name} 的详情"><div class="art-viewport">${poster(game)}</div><span class="card-name">${game.name}</span></button><p class="card-category">${game.category}</p><div class="card-actions"><button class="launch" data-action="launch" aria-label="启动 ${game.name}">启动</button><button class="icon favorite${game.favorite ? " active" : ""}" data-action="favorite" aria-label="${game.favorite ? "取消收藏" : "收藏"} ${game.name}" aria-pressed="${game.favorite}">${game.favorite ? "★" : "☆"}</button></div></article>`).join("") : '<div class="empty"><img src="assets/library.svg" alt=""><strong>这里暂时没有游戏</strong>试试其他名称或分类。</div>';
  }
  function closeDetail() {
    drawerTimeline?.kill(); gsap.killTweensOf(drawer.querySelectorAll("*")); gsap.killTweensOf(drawer);
    selected = null; drawer.hidden = true; $("#library-area").classList.remove("detail-open");
    results.querySelectorAll(".card").forEach(card => card.classList.remove("is-selected"));
    gsap.set(drawer, { clearProps: "transform,opacity,visibility" });
  }
  function openDetail(game) {
    drawerTimeline?.kill(); gsap.killTweensOf([drawer, ...drawer.querySelectorAll("*")]);
    selected = game.id;
    results.querySelectorAll(".card").forEach(card => card.classList.toggle("is-selected", Number(card.dataset.id) === game.id));
    $("#detail-name").textContent = game.name; $("#detail-category").textContent = game.category;
    $("#drawer-hero").innerHTML = poster(game); $("#detail-path").textContent = `D:\\Games\\${game.name}\\Launcher.exe`;
    $("#detail-favorite").textContent = game.favorite ? "取消收藏" : "加入收藏";
    drawer.hidden = false; $("#library-area").classList.add("detail-open");
    const hero = $("#drawer-hero"), body = $(".drawer-body");
    if (reduced()) { gsap.set([drawer, hero, body], { clearProps: "transform,opacity,visibility" }); return; }
    drawerTimeline = gsap.timeline({ defaults: { ease: "power2.out", clearProps: "transform,opacity,visibility" } })
      .fromTo(drawer, { x: 14, autoAlpha: .72 }, { x: 0, autoAlpha: 1, duration: duration(.18) }, 0)
      .fromTo(hero, { autoAlpha: .65 }, { autoAlpha: 1, duration: duration(.16) }, duration(.015))
      .fromTo(body, { y: 6, autoAlpha: .72 }, { y: 0, autoAlpha: 1, duration: duration(.15) }, duration(.03));
  }
  function favorite(game) {
    game.favorite = !game.favorite;
    const source = results.querySelector(`[data-id="${game.id}"] .favorite`);
    if (navigation === "favorite") { render(); reveal(results); }
    else if (source) {
      source.textContent = game.favorite ? "★" : "☆"; source.classList.toggle("active", game.favorite);
      source.setAttribute("aria-pressed", game.favorite); source.setAttribute("aria-label", `${game.favorite ? "取消收藏" : "收藏"} ${game.name}`);
      animate(source, { scale: 1, duration: .14, clearProps: "transform" });
    }
    $("#favorites-count").textContent = games.filter(item => item.favorite).length;
    $("#stat-favorites").textContent = games.filter(item => item.favorite).length;
    if (selected === game.id) $("#detail-favorite").textContent = game.favorite ? "取消收藏" : "加入收藏";
    notice(game.favorite ? "已加入收藏。" : "已取消收藏。");
  }
  function launch(game) { game.recent = true; notice(`预览：已提交「${game.name}」的启动请求。`); }
  results.addEventListener("click", event => {
    const action = event.target.closest("button[data-action]"); if (!action) return;
    stopDemo(); const game = games[Number(action.closest(".card").dataset.id)];
    if (action.dataset.action === "detail") openDetail(game);
    if (action.dataset.action === "favorite") favorite(game);
    if (action.dataset.action === "launch") launch(game);
  });
  // Delegated pointer feedback also works after list recycling; hit boxes keep their layout.
  app.addEventListener("pointerdown", event => {
    const button = event.target.closest("button"); if (!button || button.disabled) return;
    animate(button, { scale: button.classList.contains("icon") ? .9 : .985, duration: .085 });
  });
  const release = () => app.querySelectorAll("button").forEach(button => { if (gsap.getProperty(button, "scaleX") !== 1) animate(button, { scale: 1, duration: .14, clearProps: "transform" }); });
  window.addEventListener("pointerup", release); window.addEventListener("pointercancel", release); window.addEventListener("blur", release);
  app.addEventListener("pointerover", event => {
    const card = event.target.closest(".card");
    if (card && !card.contains(event.relatedTarget) && !reduced()) {
      animate(card, { y: listMode ? -2 : -5, rotationX: listMode ? 0 : 3, rotationY: listMode ? 0 : -3, duration: .24 });
      const sheen = card.querySelector(".card-sheen");
      gsap.fromTo(sheen, { xPercent: -120, opacity: 1 }, { xPercent: 120, duration: duration(.7), ease: "power2.out", overwrite: true, onComplete: () => gsap.set(sheen, { opacity: 0, clearProps: "transform" }) });
    }
    const viewport = event.target.closest(".art-viewport"); if (!viewport || viewport.contains(event.relatedTarget) || listMode) return;
    animate(viewport.querySelector(".poster"), { scale: 1.025, duration: .16 });
  });
  app.addEventListener("pointerout", event => {
    const card = event.target.closest(".card");
    if (card && !card.contains(event.relatedTarget)) {
      animate(card, { y: 0, rotationX: 0, rotationY: 0, duration: .18, clearProps: "transform" });
      gsap.killTweensOf(card.querySelector(".card-sheen")); gsap.set(card.querySelector(".card-sheen"), { opacity: 0, clearProps: "transform" });
    }
    const viewport = event.target.closest(".art-viewport"); if (!viewport || viewport.contains(event.relatedTarget)) return;
    animate(viewport.querySelector(".poster"), { scale: 1, duration: .18, clearProps: "transform" });
  });
  document.querySelectorAll("[data-nav]").forEach(button => button.addEventListener("click", () => {
    stopDemo(); closeDetail(); navigation = button.dataset.nav;
    document.querySelectorAll("[data-nav]").forEach(item => { item.classList.toggle("selected", item === button); if (item === button) item.setAttribute("aria-current", "page"); else item.removeAttribute("aria-current"); });
    $("#page-title").textContent = navigation === "all" ? "游戏库" : navigation === "favorite" ? "我的收藏" : "最近启动";
    $("#page-subtitle").textContent = navigation === "all" ? "把喜欢的世界，放在一起。" : navigation === "favorite" ? "喜欢的游戏，随时回到身边。" : "每一次冒险，都有迹可循。";
    render(); reveal($(".page-header"), .14); reveal(results);
  }));
  $("#search").addEventListener("input", () => { stopDemo(); render(); });
  for (const id of ["category", "sort"]) $(`#${id}`).addEventListener("change", () => { stopDemo(); render(); reveal(results); });
  $("#view").addEventListener("click", () => { stopDemo(); listMode = !listMode; $("#view").textContent = listMode ? "封面" : "列表"; render(); reveal(results); });
  $("#close-detail").addEventListener("click", () => { stopDemo(); const id = selected; closeDetail(); results.querySelector(`[data-id="${id}"] .cover-button`)?.focus({ preventScroll: true }); });
  $("#detail-favorite").addEventListener("click", () => { stopDemo(); if (selected !== null) favorite(games[selected]); });
  $("#detail-launch").addEventListener("click", () => { stopDemo(); if (selected !== null) launch(games[selected]); });
  $("#add").addEventListener("click", () => notice("这是动效预览。请在桌面工具中选择 .exe 或 .lnk 添加游戏。"));
  $("#reduce").addEventListener("change", resetMotion); $("#speed").addEventListener("change", resetMotion);
  $("#replay").addEventListener("click", () => { resetMotion(); enter(); });
  $("#demo").addEventListener("click", () => {
    if (demoTimeline) { stopDemo(); return; }
    resetMotion(); closeDetail(); listMode = false; navigation = "all"; $("#search").value = ""; $("#category").value = "all";
    $("[data-nav='all']").click(); $("#view").textContent = "列表"; render(); enter(); $("#demo").textContent = "□ 停止演示";
    demoTimeline = gsap.timeline({ onComplete: () => { demoTimeline = null; $("#demo").textContent = "▷ 自动演示"; } })
      .call(() => animate(results.querySelector(".poster"), { scale: 1.025, duration: .16 }), [], .65)
      .call(() => animate(results.querySelector(".poster"), { scale: 1, duration: .18, clearProps: "transform" }), [], 1.3)
      .call(() => openDetail(games[0]), [], 1.7)
      .call(() => openDetail(games[1]), [], 2.7)
      .call(() => closeDetail(), [], 3.7)
      .call(() => notice("动效演示完成。悬停或点击，继续体验。"), [], 4.1);
  });
  document.addEventListener("keydown", event => {
    if (event.key === "Escape") { stopDemo(); closeDetail(); }
    if (event.ctrlKey && event.key.toLowerCase() === "f") { event.preventDefault(); $("#search").focus(); $("#search").select(); }
    if ((event.key === " " || event.key === "Enter") && event.target.matches("button")) animate(event.target, { scale: event.target.classList.contains("icon") ? .9 : .985, duration: .085 });
  });
  document.addEventListener("keyup", release);
  window.addEventListener("blur", () => { foreground = false; resetMotion(); }); window.addEventListener("focus", () => { foreground = true; startAmbient(); });
  document.addEventListener("visibilitychange", () => { if (document.hidden) resetMotion(); else startAmbient(); });
  window.addEventListener("pagehide", () => { resetMotion(); media.revert(); });
  render(); enter();
})();
