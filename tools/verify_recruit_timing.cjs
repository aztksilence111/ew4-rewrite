'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs');
require('../rewrite/Ew4.Web/wwwroot/map-surface.js');
require('../rewrite/Ew4.Web/wwwroot/battlefield.js');
const native=JSON.parse(fs.readFileSync(process.argv[2]||'build/recruit-timing-20261101/native.json','utf8'));
const {recruitSpeed,recruitDuration,recruitOffset}=Ew4Battlefield;
let checks=0;
for(const vector of native.vectors){
  const speed=recruitSpeed(vector.setting,vector.ai),duration=recruitDuration(speed);
  for(const sample of vector.samples){assert.ok(Math.abs(recruitOffset(sample.wallMs,speed)-sample.offset)<.0001,JSON.stringify({vector,sample}));checks++;}
  assert.ok(duration<=vector.landedByMs&&duration>vector.landedByMs-10);checks++;
  assert.equal(recruitOffset(duration,speed),0);checks++;
  assert.equal(recruitOffset(duration+1000,speed),0);checks++;
}
for(let setting=1;setting<=5;setting++){assert.equal(recruitSpeed(setting),1.15);checks++;}
console.log(JSON.stringify({checks,errors:0,humanDurationMs:recruitDuration(),source:'Original ARM64 JNI clock and recruit descent samples'}));
