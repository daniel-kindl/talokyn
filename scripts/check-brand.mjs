#!/usr/bin/env node
import { readFile } from "node:fs/promises";
import { pathToFileURL } from "node:url";
import { readdir } from "node:fs/promises";
import { join } from "node:path";
import { updateTokens } from "../scripts/generate-brand-tokens.mjs";
const tokenUrl = new URL("../design/brand/tokens.json", import.meta.url);
const assetsUrl = new URL("../assets/brand/", import.meta.url);
const data = JSON.parse(await readFile(tokenUrl, "utf8"));
const get = (path) => path.split(".").reduce((node, part) => node[part], data);
const resolve = (value) => typeof value === "string" && /^\{.+\}$/.test(value) ? resolve(get(value.slice(1,-1)).$value) : value;
const relativeLuminance = (hex) => {
  const rgb = [1,3,5].map((i) => parseInt(hex.slice(i,i+2), 16) / 255);
  const corrected = rgb.map((n) => n <= 0.04045 ? n/12.92 : Math.pow((n + .055) / 1.055, 2.4));
  return corrected[0] * .2126 + corrected[1] * .7152 + corrected[2] * .0722;
};
const contrast = (a,b) => {
  const x = relativeLuminance(resolve(a));
  const y = relativeLuminance(resolve(b));
  return (Math.max(x,y)+.05)/(Math.min(x,y)+.05);
};
let assertions = 0;
const expect = (value,msg) => {assertions++;if(!value)throw new Error(msg)};
await updateTokens(true);
const pairs=[
  ["dark.fg.primary","dark.bg.canvas",4.5],
  ["dark.fg.secondary","dark.bg.surface",4.5],
  ["dark.fg.muted","dark.bg.raised",4.5],
  ["dark.action.primaryFg","dark.action.primaryBg",4.5],
  ["dark.action.secondaryFg","dark.action.secondaryBg",4.5],
  ["dark.focus.ring","dark.bg.canvas",3],
  ["light.fg.primary","light.bg.canvas",4.5],
  ["light.fg.secondary","light.bg.surface",4.5],
  ["light.fg.muted","light.bg.raised",4.5],
  ["light.action.primaryFg","light.action.primaryBg",4.5],
  ["light.focus.ring","light.bg.canvas",3]
];
for(const [a,b,min] of pairs){
  const ratio = contrast(get("theme."+a).$value,get("theme."+b).$value);
  expect(ratio >= min, a+" / "+b+": contrast "+ratio.toFixed(2)+" below "+min);
}
for(const category of ["logo","banners","patterns","icons"]){
  const directory=join(assetsUrl.pathname,category);
  const files=(await readdir(directory)).filter((name)=>name.endsWith(".svg"));
  expect(files.length>0,category+" has no SVG assets");
  for(const file of files){
    const xml=await readFile(join(directory,file),"utf8");
    expect(xml.includes("<svg ") && /viewBox="0 0 [0-9]+ [0-9]+"/.test(xml),file+" lacks SVG viewBox");
    expect(!/<script\b|<foreignObject\b|onload=|<image\b|href="https?:/i.test(xml),file+" has executable/external content");
    expect(/<title>.+<\/title>/.test(xml),file+" lacks accessible title");
  }
}
console.log("Brand checks passed ("+assertions+" assertions).");
