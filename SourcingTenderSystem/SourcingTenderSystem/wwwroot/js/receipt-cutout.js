// Removes an edge-connected light studio background locally, preserving enclosed whites.
(() => {
window.stmsCutout = async image => {
 const canvas=document.createElement('canvas'),scale=Math.min(1,640/Math.max(image.naturalWidth,image.naturalHeight));
 canvas.width=Math.round(image.naturalWidth*scale);canvas.height=Math.round(image.naturalHeight*scale);
 const ctx=canvas.getContext('2d');ctx.drawImage(image,0,0,canvas.width,canvas.height);
 const pixels=ctx.getImageData(0,0,canvas.width,canvas.height),d=pixels.data,w=canvas.width,h=canvas.height;
 const seen=new Uint8Array(w*h),queue=new Int32Array(w*h);let head=0,tail=0;
 function light(i){const p=i*4,lo=Math.min(d[p],d[p+1],d[p+2]),hi=Math.max(d[p],d[p+1],d[p+2]);return lo>215&&hi-lo<32;}
 function add(i){if(!seen[i]&&light(i)){seen[i]=1;queue[tail++]=i;}}
 for(let x=0;x<w;x++){add(x);add((h-1)*w+x);}for(let y=0;y<h;y++){add(y*w);add(y*w+w-1);}
 while(head<tail){const i=queue[head++],x=i%w,y=Math.floor(i/w);d[i*4+3]=0;if(x)add(i-1);if(x<w-1)add(i+1);if(y)add(i-w);if(y<h-1)add(i+w);}
 // Soften the remaining pale boundary without touching enclosed shirt highlights.
 for(let y=1;y<h-1;y++)for(let x=1;x<w-1;x++){const i=y*w+x;if(seen[i]||!(seen[i-1]||seen[i+1]||seen[i-w]||seen[i+w]))continue;const p=i*4,lo=Math.min(d[p],d[p+1],d[p+2]);if(lo>155)d[p+3]=Math.round(d[p+3]*Math.max(.15,(255-lo)/100));}
 ctx.putImageData(pixels,0,0);return canvas.toDataURL('image/png');
};
})();
