"""
This script runs the application using a development server.
It contains the definition of routes and views for the application.
"""

import asyncio
from flask import Flask
from walletconnect import WCSession
from werkzeug.serving import is_running_from_reloader
app = Flask(__name__)

# Make the WSGI interface available at the top level so wfastcgi can get it.
wsgi_app = app.wsgi_app

loop = asyncio.get_event_loop()
print(f'{loop}')

async def connect_dapp():
    connection_request = await WCSession.connect_session('wc:11bf72e9-3b02-4b3d-8abb-30bd7219002a@1?bridge=https%3A%2F%2Fwcbridge.garyng.com&key=1cb19e5990e5232a045c944fc66bbad3709bfec5f5aabe0d13b3023233033941',mymeta={},jsonrpchandler={},chainid=1,timeout=0)
    print(f'{connection_request}')
    (wcsession, session_request) = await connection_request
    x = await wcsession.send_sessionrequestresponse(session_request, mymeta={})
    print(f'{x}')
    pass

async def connect_wallet():
    connection = await WCSession.create_session('https://wcbridge.garyng.com', mymeta={},jsonrpchandler={},chainid=1,timeout=0)

    print(f'{connection}')
    print(f'{connection.wcuri}')
    (wcsession, jsonrpcresult, sessionrequest) = await connection.sessionrequest
    print(f'{wcsession} {jsonrpcresult} {sessionrequest}')
    pass

@app.route('/')
def hello():
    """Renders a sample page."""
    worker_loop = asyncio.new_event_loop()
    #asyncio.set_event_loop(worker_loop)
    #loop = asyncio.get_event_loop()
    #print(f'{loop}')
    #loop.run_until_complete(connect_wallet())
    asyncio.run(connect_dapp())
    return "Hello World!"

if __name__ == '__main__':
    import os
    HOST = os.environ.get('SERVER_HOST', 'localhost')
    try:
        PORT = int(os.environ.get('SERVER_PORT', '5555'))
    except ValueError:
        PORT = 5555
    app.run(HOST, PORT)
