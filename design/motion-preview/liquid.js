"use strict";
(() => {
  const canvas = document.querySelector("#liquid-canvas"), host = document.querySelector("#app"), fallback = document.querySelector(".liquid-fallback");
  let gl, program, resolution, timeLocation, request = null, running = false;
  let enabled = true, foreground = document.hasFocus(), lost = false, elapsed = 0, last = 0, frames = 0;
  const flow = new LiquidFlowField();
  let flowTexture, previousPointer=null, pendingPointer=null, pointerTime=0, accumulator=0;
  const fragment = `precision highp float;
uniform vec2 uResolution; uniform float uTime; uniform sampler2D uFlow;
void main() {
    vec2 uv = gl_FragCoord.xy / uResolution; uv.y = 1.0 - uv.y;
    float aspect = uResolution.x / uResolution.y;
    vec2 lookup = (uv * vec2(63.0, 39.0) + 0.5) / vec2(128.0, 40.0);
    vec2 packedX = texture2D(uFlow, lookup).rg;
    vec2 packedY = texture2D(uFlow, lookup + vec2(0.5, 0.0)).rg;
    vec2 displacement = vec2(dot(packedX, vec2(256.0, 1.0)), dot(packedY, vec2(256.0, 1.0))) / 257.0 - 0.5;
    float t = uTime * 0.18;
    vec2 p = (uv + displacement - 0.5) * vec2(aspect, 1.0) * 2.4;
    for (int i = 1; i <= 3; i++)
    {
        float k = float(i);
        p += vec2(sin(p.y * 1.65 + k * 1.2 + t * 0.7), cos(p.x * 1.45 - k * 0.9 - t * 0.6)) * (0.65 / k);
    }
    float phase = p.x * 1.6 + p.y * 1.1 + 0.65 * sin(p.y * 1.8 - p.x * 0.7 + t * 0.3);
    float band = 0.5 + 0.5 * sin(phase);
    vec3 color = mix(vec3(0.065, 0.072, 0.085), vec3(0.46, 0.49, 0.535), smoothstep(0.06, 0.94, band));
    float sheen = pow(0.5 + 0.5 * sin(phase + 0.55), 14.0) * 0.22;
    color = mix(color, vec3(0.76, 0.79, 0.835), sheen);
    vec2 center = uv - 0.5;
    gl_FragColor = vec4(color * (0.96 - 0.3 * dot(center, center)), 1.0);
}
`;
  function compile(type, source) {
    const shader = gl.createShader(type); gl.shaderSource(shader, source); gl.compileShader(shader);
    if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) { gl.deleteShader(shader); throw new Error("Liquid shader compilation failed"); }
    return shader;
  }
  function initialize() {
    gl = canvas.getContext("webgl", { alpha: false, antialias: false, powerPreference: "low-power", preserveDrawingBuffer: true });
    if (!gl) { canvas.hidden = true; fallback.hidden = false; return; }
    try {
      const vertex = compile(gl.VERTEX_SHADER, "attribute vec2 position; void main(){gl_Position=vec4(position,0.0,1.0);}");
      const pixel = compile(gl.FRAGMENT_SHADER, fragment);
      program = gl.createProgram(); gl.attachShader(program, vertex); gl.attachShader(program, pixel); gl.linkProgram(program);
      gl.deleteShader(vertex); gl.deleteShader(pixel);
      if (!gl.getProgramParameter(program, gl.LINK_STATUS)) throw new Error("Liquid shader linking failed");
      gl.useProgram(program);
      const buffer = gl.createBuffer(); gl.bindBuffer(gl.ARRAY_BUFFER, buffer); gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1,-1,3,-1,-1,3]), gl.STATIC_DRAW);
      const position = gl.getAttribLocation(program, "position"); gl.enableVertexAttribArray(position); gl.vertexAttribPointer(position,2,gl.FLOAT,false,0,0);
      resolution = gl.getUniformLocation(program,"uResolution"); timeLocation = gl.getUniformLocation(program,"uTime");
      flowTexture = gl.createTexture(); gl.activeTexture(gl.TEXTURE0); gl.bindTexture(gl.TEXTURE_2D,flowTexture);
      gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MIN_FILTER,gl.LINEAR); gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MAG_FILTER,gl.LINEAR);
      gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_WRAP_S,gl.CLAMP_TO_EDGE); gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_WRAP_T,gl.CLAMP_TO_EDGE);
      gl.texImage2D(gl.TEXTURE_2D,0,gl.RGBA,flow.w*2,flow.h,0,gl.RGBA,gl.UNSIGNED_BYTE,flow.encode());
      gl.uniform1i(gl.getUniformLocation(program,"uFlow"),0);
      canvas.hidden = false; fallback.hidden = true; resize(); sync();
    } catch { gl = null; canvas.hidden = true; fallback.hidden = false; }
  }
  function resize() {
    if (!gl || lost) return;
    const scale = Math.min(1, 1280 / canvas.clientWidth, 900 / canvas.clientHeight);
    canvas.width = Math.max(1, Math.round(canvas.clientWidth * scale)); canvas.height = Math.max(1, Math.round(canvas.clientHeight * scale));
    gl.viewport(0,0,canvas.width,canvas.height); draw();
  }
  function updatePointer() {
    if (!pendingPointer || !running) return;
    const next=pendingPointer; pendingPointer=null;
    if (previousPointer && elapsed-pointerTime<.25) flow.stir(...previousPointer,...next,canvas.width/canvas.height);
    previousPointer=next; pointerTime=elapsed;
  }
  function draw() {
    gl.uniform2f(resolution,canvas.width,canvas.height); gl.uniform1f(timeLocation,elapsed);
    gl.texSubImage2D(gl.TEXTURE_2D,0,0,0,flow.w*2,flow.h,gl.RGBA,gl.UNSIGNED_BYTE,flow.encode());
    gl.drawArrays(gl.TRIANGLES,0,3); frames++;
  }
  function tick(now) {
    if (!running) return;
    const dt=Math.min(.05,(now-last)/1000); last=now; elapsed+=dt;
    updatePointer(); accumulator+=dt;
    while(accumulator>=1/60) { flow.step(1/60,canvas.width/canvas.height); accumulator-=1/60; }
    draw();
    request=requestAnimationFrame(tick);
  }
  function stop() { running = false; previousPointer=null; pendingPointer=null; if (request !== null) cancelAnimationFrame(request); request = null; }
  function sync() {
    const canRun = !!gl && !lost && enabled && foreground && !document.hidden && !matchMedia("(prefers-reduced-motion: reduce)").matches;
    if (!canRun) { stop(); return; }
    if (running) return;
    running = true; last = performance.now(); request = requestAnimationFrame(tick);
  }
  window.cxLiquid = {
    setEnabled(value) { enabled = value; sync(); },
    get running() { return running; }, get mode() { return gl && !lost ? "webgl" : "static-fallback"; },
    get elapsed() { return elapsed; }, get frames() { return frames; },
    get disturbances() { return flow.energy > .000001 ? 1 : 0; },
    get energy() { return flow.energy; },
    get pixels() { const data = new Uint8Array(4); if (gl && !lost) gl.readPixels(Math.floor(canvas.width*.4),Math.floor(canvas.height*.6),1,1,gl.RGBA,gl.UNSIGNED_BYTE,data); return [...data]; }
  };
  window.addEventListener("blur", () => { foreground = false; sync(); });
  window.addEventListener("focus", () => { foreground = true; sync(); });
  document.addEventListener("visibilitychange",sync);
  matchMedia("(prefers-reduced-motion: reduce)").addEventListener("change",sync);
  window.addEventListener("pagehide",stop);
  host.addEventListener("pointermove",event=>{
    if (!running) return;
    const rect=canvas.getBoundingClientRect(), x=(event.clientX-rect.left)/rect.width, y=(event.clientY-rect.top)/rect.height;
    if(x>=0 && x<=1 && y>=0 && y<=1) pendingPointer=[x,y];
  });
  host.addEventListener("pointerleave",()=>{previousPointer=null;pendingPointer=null;});
  canvas.addEventListener("webglcontextlost", event => { event.preventDefault(); lost = true; canvas.hidden = true; fallback.hidden = false; stop(); });
  canvas.addEventListener("webglcontextrestored", () => { lost = false; initialize(); });
  new ResizeObserver(resize).observe(host);
  initialize();
})();
