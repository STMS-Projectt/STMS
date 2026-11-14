// Loopback stand-in for Face++ (detect/compare) so the browser flow can be exercised without credentials.
import http from 'node:http';
let detects = 0, reject = false, calls = [];
http.createServer((req, res) => {
  let body = '';
  req.on('data', c => body += c);
  req.on('end', () => {
    const url = new URL(req.url, 'http://x');
    res.setHeader('Content-Type', 'application/json');
    if (url.pathname === '/control') { reject = url.searchParams.get('reject') === '1'; detects = 0; return res.end(JSON.stringify({ reject, calls })); }
    calls.push(url.pathname.split('/').pop());
    const hasKey = body.includes('api_key=') && body.includes('api_secret=');
    if (!hasKey) { res.statusCode = 401; return res.end('{"error_message":"AUTHENTICATION_ERROR"}'); }
    if (url.pathname.endsWith('/detect')) {
      const yaw = [0, 25, 0][detects++ % 3];
      return res.end(JSON.stringify({ faces: [{ face_token: 'mock' + detects, attributes: { headpose: { yaw_angle: yaw } } }] }));
    }
    const reference = body.includes('image_base64_2=');
    res.end(JSON.stringify({ confidence: reject && reference ? 31.2 : 92.4, thresholds: { '1e-3': 62.3, '1e-4': 69.1, '1e-5': 73.9 } }));
  });
}).listen(18990, '127.0.0.1');
