"""Independent loopback-only fixture. All content stays in the explicitly supplied root."""
import argparse
import os
from pathlib import Path

import cheroot
import wsgidav
from cheroot import wsgi
from wsgidav.wsgidav_app import WsgiDAVApp

if wsgidav.__version__ != "4.3.5" or cheroot.__version__ != "11.1.2":
    raise RuntimeError("Install the fixture's pinned requirements before running")

parser = argparse.ArgumentParser()
parser.add_argument("--root", required=True)
parser.add_argument("--port", type=int, default=19080)
parser.add_argument("--basic", action="store_true")
args = parser.parse_args()
root = Path(args.root).resolve()
root.mkdir(parents=True, exist_ok=True)
users = True
if args.basic:
    username = os.environ["WEBDAV_INTEROP_USER"]
    password = os.environ["WEBDAV_INTEROP_PASSWORD"]
    if not username or not password:
        raise ValueError("Explicit fixture credentials required")
    users = {username: {"password": password, "roles": []}}
config = {
    "host": "127.0.0.1",
    "port": args.port,
    "provider_mapping": {"/": str(root)},
    "simple_dc": {"user_mapping": {"*": users}},
    "http_authenticator": {
        "domain_controller": None,
        "accept_basic": args.basic,
        "accept_digest": False,
        "default_to_digest": False,
    },
    "property_manager": True,
    "lock_storage": True,
    "verbose": 0,
    "logging": {"enable": False},
}
app = WsgiDAVApp(config)
server = wsgi.Server(("127.0.0.1", args.port), app)
try:
    server.start()
except KeyboardInterrupt:
    server.stop()
