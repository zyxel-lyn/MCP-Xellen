import http from "node:http";
import fs from "node:fs";
import crypto from "node:crypto";
import { spawn } from "node:child_process";

const host = "127.0.0.1";
const port = Number(process.env.XELLEN_PORT || 8021);
const internalPort = Number(process.env.XELLEN_INTERNAL_PORT || 8022);
const issuer = process.env.XELLEN_ISSUER || ("http://" + host + ":" + port);
const resource = issuer + "/mcp";
const pairingCode = process.env.XELLEN_PAIRING_CODE || "";
const secretFile = process.env.XELLEN_API_KEY_FILE;
const gatewayPath = process.env.XELLEN_GATEWAY;
const dcEntry = process.env.XELLEN_DC_ENTRY;
const root = process.env.XELLEN_ROOT;
if (!/^\d{6}$/.test(pairingCode) || !secretFile || !gatewayPath || !dcEntry || !root) throw new Error("Missing or invalid Xellen runtime configuration");
const apiKey = fs.readFileSync(secretFile, "utf8").split(/\r?\n/).map(x => x.trim()).find(Boolean);
if (!apiKey) throw new Error("MCP API key file is empty");

const gateway = spawn(process.execPath, [gatewayPath, "--stdio", 'node "' + dcEntry + '" --no-onboarding', "--outputTransport", "streamableHttp", "--port", String(internalPort), "--host", host, "--apiKeyFile", secretFile, "--stateful", "--sessionTimeout", "1800000", "--healthEndpoint", "/healthz"], { cwd: root, windowsHide: true, stdio: ["ignore", "pipe", "pipe"] });
gateway.stdout.on("data", b => process.stdout.write(b));
gateway.stderr.on("data", b => process.stderr.write(b));
gateway.on("exit", code => { console.error("[Xellen] Supergateway exited: " + code); process.exitCode = code || 1; server.close(); });

const clients = new Map(), codes = new Map(), access = new Map(), refresh = new Map();
let pairingExpiry = Date.now() + 600000, pairingUsed = false, failedPairAttempts = 0;
const rand = (n=32) => crypto.randomBytes(n).toString("base64url");
const hash = s => crypto.createHash("sha256").update(s).digest("base64url");
const reply = (res, status, obj, extra={}) => { res.writeHead(status, {"Content-Type":"application/json","Cache-Control":"no-store",...extra}); res.end(JSON.stringify(obj)); };
const rawBody = req => new Promise((resolve,reject)=>{let s="";req.setEncoding("utf8");req.on("data",x=>{s+=x;if(s.length>65536)req.destroy();});req.on("end",()=>resolve(s));req.on("error",reject);}); const body = req => rawBody(req).then(s => new URLSearchParams(s));
const esc = s => String(s).replace(/[&<>"']/g,c=>({"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;","'":"&#39;"}[c]));
const html = (title,content) => '<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>'+esc(title)+'</title><style>body{font:16px Segoe UI,Arial;background:#f4f5f7;display:grid;place-items:center;min-height:100vh;margin:0}.card{background:white;padding:30px;border-radius:14px;max-width:420px;width:calc(100% - 60px)}input{box-sizing:border-box;width:100%;padding:14px;font-size:24px;letter-spacing:8px;text-align:center}button{width:100%;padding:14px;margin-top:16px;background:#171717;color:white;border:0;border-radius:8px}</style></head><body><main class="card">'+content+'</main></body></html>';
const server = http.createServer(async (req,res) => {
 try {
  const u = new URL(req.url || "/", issuer), p = u.pathname;
  if(req.method==="GET" && p==="/healthz"){
   const ok=await fetch("http://"+host+":"+internalPort+"/healthz",{signal:AbortSignal.timeout(1500)}).then(r=>r.ok).catch(()=>false);
   return reply(res,ok?200:503,{status:ok?"ok":"starting",oauth:"ready"});
  }
  if(req.method==="GET" && (p==="/.well-known/oauth-authorization-server"||p==="/.well-known/oauth-authorization-server/mcp"))
   return reply(res,200,{issuer,authorization_endpoint:issuer+"/authorize",token_endpoint:issuer+"/token",registration_endpoint:issuer+"/register",revocation_endpoint:issuer+"/revoke",response_types_supported:["code"],grant_types_supported:["authorization_code","refresh_token"],code_challenge_methods_supported:["S256"],token_endpoint_auth_methods_supported:["none"],scopes_supported:["mcp"]});
  if(req.method==="GET" && (p==="/.well-known/oauth-protected-resource"||p==="/.well-known/oauth-protected-resource/mcp"))
   return reply(res,200,{resource,authorization_servers:[issuer],bearer_methods_supported:["header"],scopes_supported:["mcp"],resource_name:"MCP Xellen"});
  if(req.method==="POST" && p==="/register"){
   const raw=await rawBody(req);let b;try{b=JSON.parse(raw);}catch{return reply(res,400,{error:"invalid_client_metadata"});}const uris=b.redirect_uris||[];
   if(!Array.isArray(uris)||!uris.length||uris.some(x=>typeof x!=="string"||!/^https?:\/\//.test(x)))return reply(res,400,{error:"invalid_redirect_uri"});
   const id=rand(18),c={client_id:id,client_name:b.client_name||"MCP Client",redirect_uris:uris,grant_types:["authorization_code","refresh_token"],response_types:["code"],token_endpoint_auth_method:"none"};
   clients.set(id,c);return reply(res,201,c);
  }
  if(req.method==="GET" && p==="/authorize"){
   const c=clients.get(u.searchParams.get("client_id")),redirect=u.searchParams.get("redirect_uri")||"",challenge=u.searchParams.get("code_challenge")||"";
   if(!c||!c.redirect_uris.includes(redirect)||u.searchParams.get("response_type")!=="code"||!challenge||u.searchParams.get("code_challenge_method")!=="S256")return reply(res,400,{error:"invalid_request"});
   const fields={client_id:c.client_id,redirect_uri:redirect,state:u.searchParams.get("state")||"",code_challenge:challenge,resource:u.searchParams.get("resource")||resource};
   const hidden=Object.entries(fields).map(([k,v])=>'<input type="hidden" name="'+esc(k)+'" value="'+esc(v)+'">').join("");
   res.writeHead(200,{"Content-Type":"text/html; charset=utf-8","Cache-Control":"no-store"});
   return res.end(html("Authorize MCP Xellen",'<h2>Connect to MCP Xellen</h2><p>Masukkan kode pairing 6 digit dari aplikasi MCP Xellen.</p><form method="POST" action="/authorize/approve"><input name="pairing_code" inputmode="numeric" pattern="[0-9]{6}" maxlength="6" autocomplete="one-time-code" required>'+hidden+'<button>Authorize Z.ai</button></form><p>Kode berlaku 10 menit dan hanya bisa digunakan sekali.</p>'));
  }
  if(req.method==="POST" && p==="/authorize/approve"){
   const b=await body(req),c=clients.get(b.get("client_id")),redirect=b.get("redirect_uri")||"";
   if(!c||!c.redirect_uris.includes(redirect)||pairingUsed||Date.now()>pairingExpiry||failedPairAttempts>=5||b.get("pairing_code")!==pairingCode){failedPairAttempts++;res.writeHead(403,{"Content-Type":"text/html"});return res.end(html("Pairing failed","<h2>Pairing gagal</h2><p>Kode salah, kedaluwarsa, atau sudah digunakan.</p>"));}
   pairingUsed=true;const code=rand(32);
   codes.set(code,{client:c.client_id,redirect,challenge:b.get("code_challenge"),resource:b.get("resource")||resource,expires:Date.now()+60000});
   const cb=new URL(redirect);cb.searchParams.set("code",code);if(b.get("state"))cb.searchParams.set("state",b.get("state"));
   res.writeHead(302,{Location:cb.toString(),"Cache-Control":"no-store"});return res.end();
  }
  if(req.method==="POST" && p==="/token"){
   const b=await body(req),c=clients.get(b.get("client_id"));if(!c||b.get("client_secret"))return reply(res,401,{error:"invalid_client"});
   if(b.get("grant_type")==="authorization_code"){
    const code=b.get("code")||"",v=codes.get(code);codes.delete(code);
    if(!v||v.client!==c.client_id||v.redirect!==b.get("redirect_uri")||v.expires<Date.now()||hash(b.get("code_verifier")||"")!==v.challenge)return reply(res,400,{error:"invalid_grant"});
    const a=rand(),r=rand(40);access.set(a,{client:c.client_id,resource:v.resource,expires:Date.now()+3600000});refresh.set(r,{client:c.client_id,resource:v.resource,expires:Date.now()+2592000000});
    return reply(res,200,{access_token:a,token_type:"Bearer",expires_in:3600,refresh_token:r,scope:"mcp"});
   }
   if(b.get("grant_type")==="refresh_token"){
    const old=b.get("refresh_token")||"",v=refresh.get(old);refresh.delete(old);
    if(!v||v.client!==c.client_id||v.expires<Date.now())return reply(res,400,{error:"invalid_grant"});
    const a=rand(),r=rand(40);access.set(a,{client:c.client_id,resource:v.resource,expires:Date.now()+3600000});refresh.set(r,{client:c.client_id,resource:v.resource,expires:Date.now()+2592000000});
    return reply(res,200,{access_token:a,token_type:"Bearer",expires_in:3600,refresh_token:r,scope:"mcp"});
   }
   return reply(res,400,{error:"unsupported_grant_type"});
  }
  if(req.method==="POST" && p==="/revoke"){const b=await body(req),t=b.get("token")||"";access.delete(t);refresh.delete(t);return reply(res,200,{});}
  if(req.method==="GET" && p==="/xellen/status"){const up=await fetch("http://"+host+":"+internalPort+"/healthz",{signal:AbortSignal.timeout(1200)}).then(r=>r.ok).catch(()=>false);return reply(res,200,{gateway:up?"ready":"starting",authorized:access.size>0,activeTokens:access.size,pairingUsed});}
  if(p==="/mcp"){
   const t=/^Bearer\s+(.+)$/i.exec(req.headers.authorization||"")?.[1]||"",v=access.get(t);
   if(!v||v.expires<Date.now()||v.resource!==resource)return reply(res,401,{error:"invalid_token"},{"WWW-Authenticate":'Bearer resource_metadata="'+issuer+'/.well-known/oauth-protected-resource/mcp"'});
   const upstream=http.request({hostname:host,port:internalPort,path:"/mcp",method:req.method,headers:{...req.headers,host:host+":"+internalPort,authorization:"Bearer "+apiKey}},pr=>{res.writeHead(pr.statusCode||502,pr.headers);pr.pipe(res);});
   upstream.on("error",()=>{if(!res.headersSent)reply(res,502,{error:"upstream_error"});else res.destroy();});req.pipe(upstream);return;
  }
  return reply(res,404,{error:"not_found"});
 }catch(e){if(!res.headersSent)reply(res,500,{error:"server_error"});else res.destroy();console.error("[Xellen] "+(e.message||"request failed"));}
});
server.listen(port,host,()=>console.log("[Xellen] OAuth proxy listening on "+host+":"+port));
process.on("SIGINT",()=>{gateway.kill();server.close();});
process.on("SIGTERM",()=>{gateway.kill();server.close();});



