#!/usr/bin/env node
import { readFile, writeFile } from "node:fs/promises";
import { pathToFileURL } from "node:url";
const SOURCE = new URL("../../design/brand/tokens.json", import.meta.url);
const OUTPUT = new URL("../../design/brand/tokens.css", import.meta.url);

export function buildCss(data){
  const out=["/* Generated from design/brand/tokens.json. Do not edit manually. */"];
  const kebab=(part)=>part.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
  const flatten=(obj,path,indent="  ")=>{
    const lines=[];
    for(const [name,node] of Object.entries(obj)){
      if(name==="$metadata")continue;
      const next=[...path,name];
      if(node && typeof node==="object" && "$value" in node){
        const raw=String(node.$value);
        const value=/^\{[a-zA-Z0-9.-]+\}$/.test(raw)?`var(--tk-${raw.slice(1,-1).split(".").map(kebab).join("-")})`:raw;
        lines.push(`${indent}--tk-${next.map(kebab).join("-")}: ${value};`);
      }else if(node&&typeof node==="object"){lines.push(...flatten(node,next,indent))}
    }
    return lines;
  };
  const primitives={...data};delete primitives.theme;delete primitives.$metadata;
  out.push(":root {",...flatten(primitives,[]),"}");
  for(const [name,theme] of Object.entries(data.theme)){
    out.push("",name==="dark" ? ':root, :root[data-tk-theme="dark"] {' : ':root[data-tk-theme="light"] {',...flatten(theme,[]),"}");
  }
  out.push("","@media (prefers-reduced-motion: reduce) {","  :root {","    --tk-duration-fast: 0ms;","    --tk-duration-normal: 0ms;","    --tk-duration-slow: 0ms;","  }","}","");
  return out.join("\n");
}

export async function updateTokens(checkOnly = false) {
  const tokens = JSON.parse(await readFile(SOURCE, "utf8"));
  const expected = buildCss(tokens);
  if (checkOnly) {
    const current = await readFile(OUTPUT, "utf8");
    if (current !== expected) {
      throw new Error("Brand CSS is out of date. Run node tools/brand/generate-brand-tokens.mjs");
    }
  } else {
    await writeFile(OUTPUT, expected, "utf8");
  }
}
if (process.argv[1] && pathToFileURL(process.argv[1]).href === import.meta.url) {
  await updateTokens(process.argv.includes("--check"));
  console.log("Brand tokens are synchronized.");
}
