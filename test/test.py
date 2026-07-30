from http.server import BaseHTTPRequestHandler, HTTPServer
from threading import Thread
import requests
import time
import json



db = {
    "/number": 42,
    "/string": "Hello, World!",
    "/json": json.dumps({"a": 1, "b": 2}),
}

class Handler(BaseHTTPRequestHandler):
    def send_text_response(self, status, data="", content_type="application/json; charset=utf-8"):
        if isinstance(data, dict):
            data = json.dumps(data)
        body = str(data).encode("utf-8")
        print('Sending response:', status, body.decode("utf-8"), flush=True)
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        print("RECEIVE GET path:", self.path, flush=True)

        data = db.get(self.path)
        if data is None:
            self.send_text_response(404, "Not found")
            return

        # content_type = "application/json" if self.path == "/json" else "text/plain; charset=utf-8"
        self.send_text_response(200, data) #, content_type)

    def do_POST(self):
        content_length = int(self.headers.get("Content-Length", 0))
        data = json.loads(self.rfile.read(content_length).decode("utf-8", errors="replace"))
        print("RECEIVE POST path:", self.path, "POST data:", data, flush=True)

        db[self.path] = data

        self.send_text_response(200)

port = 8080
server = HTTPServer(("0.0.0.0", port), Handler)
running = True

def do_requests():
    port = 8082
    while running:
        time.sleep(1)  # Wait for the server to start
        path = "/data"
        print("DATA", db)
    
        try:
            # Test GET requests
            # print("Testing GET requests...")
            response = requests.get(f"http://localhost:{port}{path}", timeout=1)
            # print(f"SENT GET {path} -> Status: {response.status_code}, Response: {response.text}")
        except Exception as e:
            pass
    
        try:
        # Test POST requests
            # print("\nTesting POST requests...")
            post_data = {
                "num": db["/number"],
                "str": "string"
            }
            response = requests.post(f"http://localhost:{port}{path}", data=json.dumps(post_data), timeout=1)
            # print(f"SENT POST {path} with data '{post_data}' -> Status: {response.status_code}")
        except Exception as e:
            pass

thread = Thread(target=do_requests)
thread.start()

print(f"Listening on http://localhost:{port}", flush=True)
server.serve_forever()

running = False
