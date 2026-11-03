import {createServer} from 'node:http';
import {mkdirSync} from 'node:fs';
import {resolve} from 'node:path';
import {openDB} from './db.mjs';
import worker from '../dist/server/index.js';
const data=process.env.DATA_DIR||'.local';mkdirSync(data,{recursive:true});const DB=openDB(resolve(data,'game.sqlite'));
const server=createServer(async(req,res)=>{try{const chunks=[];let size=0;for await(const chunk of req){size+=chunk.length;if(size>4096){res.writeHead(413);res.end('Request too large');return}chunks.push(chunk)}const body=Buffer.concat(chunks);const proto=req.headers['x-forwarded-proto']==='https'?'https':'http';const headers={...req.headers};delete headers['content-length'];const request=new Request(proto+'://'+req.headers.host+req.url,{method:req.method,headers,...(body.length?{body}:{} )});const result=await worker.fetch(request,{DB},{});res.writeHead(result.status,Object.fromEntries(result.headers));res.end(Buffer.from(await result.arrayBuffer()));}catch(error){console.error('Request failed:',error.message);res.writeHead(500,{'Content-Type':'application/json'});res.end(JSON.stringify({error:'A brief server error. Please try again.'}))}});
server.listen(Number(process.env.PORT||10000),'0.0.0.0',()=>console.log('Deep & Sky is listening on the public HTTP port.'));
for(const signal of ['SIGTERM','SIGINT'])process.on(signal,()=>{server.close(()=>{DB.close();process.exit(0)});setTimeout(()=>process.exit(0),8000).unref()});
