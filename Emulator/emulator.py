import argparse
import http.server
import json
from pathlib import Path
import socket
import time
import webbrowser


class EmulatorHandler(http.server.BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass

    def reply(self, data, media):
        self.send_response(200)
        self.send_header("Content-Type", media)
        self.send_header("Cache-Control", "no-store")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        if self.headers.get("Host") != self.server.host or self.path != "/":
            self.send_error(404)
            return
        self.reply(self.server.page, "text/html; charset=utf-8")

    def do_POST(self):
        server = self.server
        if self.path != "/pad" or self.headers.get("Host") != server.host or self.headers.get("Origin") != f"http://{server.host}":
            self.send_error(403)
            return
        if self.headers.get("Content-Length") != "12":
            self.send_error(400)
            return
        packet = self.rfile.read(12)
        try:
            server.pad.send(packet)
            while True:
                server.last_feedback = server.pad.recv(512).decode()
                server.updated = time.monotonic()
        except OSError:
            pass
        current = server.last_feedback if time.monotonic() - server.updated < 0.5 else ""
        self.reply(json.dumps(current).encode(), "application/json")


class EmulatorServer(http.server.HTTPServer):
    def __init__(self, port, pad, page):
        super().__init__(("127.0.0.1", port), EmulatorHandler)
        self.host = f"127.0.0.1:{self.server_port}"
        self.pad = pad
        self.page = page
        self.last_feedback = ""
        self.updated = 0


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, default=27356)
    parser.add_argument("--no-browser", action="store_true")
    args = parser.parse_args()
    page = Path(__file__).with_name("index.html").read_bytes()
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as pad:
        pad.connect(("127.0.0.1", 27355))
        pad.setblocking(False)
        with EmulatorServer(args.port, pad, page) as server:
            url = f"http://{server.host}"
            print(url, flush=True)
            if not args.no_browser:
                webbrowser.open(url)
            server.serve_forever()


if __name__ == "__main__":
    main()
