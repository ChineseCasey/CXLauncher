"use strict";
// Same grid, fixed time step and pressure solve as the desktop LiquidFlowField.
class LiquidFlowField {
  constructor() {
    this.w=64; this.h=40;
    for (const name of ['vx','vy','dx','dy','nextVx','nextVy','nextDx','nextDy','pressure','divergence']) this[name]=new Float32Array(this.w*this.h);
    this.pixels=new Uint8Array(this.w*2*this.h*4);
  }
  sample(field,x,y) {
    x=Math.max(0,Math.min(this.w-1.001,x)); y=Math.max(0,Math.min(this.h-1.001,y));
    const ix=Math.floor(x), iy=Math.floor(y), fx=x-ix, fy=y-iy, i=iy*this.w+ix;
    return (field[i]*(1-fx)+field[i+1]*fx)*(1-fy)+(field[i+this.w]*(1-fx)+field[i+this.w+1]*fx)*fy;
  }
  stir(ax,ay,bx,by,aspect) {
    const mx=(bx-ax)*aspect, my=by-ay, length=Math.hypot(mx,my);
    if(length<.00001) return;
    const strength=Math.min(length,.12)*28;
    for(let y=1;y<this.h-1;y++) for(let x=1;x<this.w-1;x++) {
      let px=(x/(this.w-1)-ax)*aspect, py=y/(this.h-1)-ay;
      const along=Math.max(0,Math.min(1,(px*mx+py*my)/(length*length)));
      px-=along*mx; py-=along*my;
      const weight=Math.exp(-(px*px+py*py)/.008), i=y*this.w+x;
      this.vx[i]=Math.max(-1.5,Math.min(1.5,this.vx[i]+mx/length*strength*weight/aspect));
      this.vy[i]=Math.max(-1.5,Math.min(1.5,this.vy[i]+my/length*strength*weight));
    }
  }
  step(dt,aspect) {
    const w=this.w,h=this.h,damping=Math.exp(-2.4*dt),relaxation=Math.exp(-.85*dt);
    for(let y=1;y<h-1;y++) for(let x=1;x<w-1;x++) {
      const i=y*w+x, px=x-this.vx[i]*dt*(w-1), py=y-this.vy[i]*dt*(h-1);
      this.nextVx[i]=this.sample(this.vx,px,py)*damping; this.nextVy[i]=this.sample(this.vy,px,py)*damping;
    }
    [this.vx,this.nextVx]=[this.nextVx,this.vx]; [this.vy,this.nextVy]=[this.nextVy,this.vy];
    const sx=(w-1)/aspect,sy=h-1,p=this.pressure;
    p.fill(0);
    for(let y=1;y<h-1;y++) for(let x=1;x<w-1;x++) {
      const i=y*w+x;
      this.divergence[i]=.5*((this.vx[i+1]-this.vx[i-1])*aspect*sx+(this.vy[i+w]-this.vy[i-w])*sy);
    }
    for(let iteration=0;iteration<10;iteration++) for(let y=1;y<h-1;y++) for(let x=1;x<w-1;x++) {
      const i=y*w+x;
      p[i]=((p[i-1]+p[i+1])*sx*sx+(p[i-w]+p[i+w])*sy*sy-this.divergence[i])/(2*(sx*sx+sy*sy));
    }
    for(let y=1;y<h-1;y++) for(let x=1;x<w-1;x++) {
      const i=y*w+x;
      this.vx[i]-=.5*(p[i+1]-p[i-1])*sx/aspect; this.vy[i]-=.5*(p[i+w]-p[i-w])*sy;
      const px=x-this.vx[i]*dt*(w-1), py=y-this.vy[i]*dt*(h-1);
      this.nextDx[i]=Math.max(-.45,Math.min(.45,(this.sample(this.dx,px,py)-this.vx[i]*dt)*relaxation));
      this.nextDy[i]=Math.max(-.45,Math.min(.45,(this.sample(this.dy,px,py)-this.vy[i]*dt)*relaxation));
    }
    [this.dx,this.nextDx]=[this.nextDx,this.dx]; [this.dy,this.nextDy]=[this.nextDy,this.dy];
  }
  get energy() { let total=0; for(let i=0;i<this.vx.length;i++) total+=this.vx[i]**2+this.vy[i]**2; return total/this.vx.length; }
  encode() {
    for(let i=0;i<this.dx.length;i++) {
      let offset=(Math.floor(i/this.w)*this.w*2+i%this.w)*4;
      const x=Math.round((this.dx[i]+.5)*65535), y=Math.round((this.dy[i]+.5)*65535);
      this.pixels[offset]=x>>8; this.pixels[offset+1]=x&255; this.pixels[offset+2]=0; this.pixels[offset+3]=255; offset+=this.w*4;
      this.pixels[offset]=y>>8; this.pixels[offset+1]=y&255; this.pixels[offset+2]=0; this.pixels[offset+3]=255;
    }
    return this.pixels;
  }
}
