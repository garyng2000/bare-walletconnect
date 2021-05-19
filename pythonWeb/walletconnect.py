from hashlib import md5
from Crypto.Cipher import AES
from Crypto.Random import get_random_bytes
from Crypto.Util.Padding import pad, unpad

import json
import jsons
import asyncio
from asyncio import Future
import time
import websockets
import uuid
import urllib
import hmac
import hashlib
import re
from typing import Any, List, Set, Dict, Tuple, Optional, Callable, Coroutine, Awaitable
from datetime import datetime, timezone, timedelta


def to_json(obj):
    return jsons.dumps(obj)

def jsnow():
    now = datetime.now(timezone.utc)
    epoch = datetime(1970, 1, 1, tzinfo=timezone.utc) # use POSIX epoch
    posix_timestamp_micros = (now - epoch) // timedelta(microseconds=1)
    posix_timestamp_millis = posix_timestamp_micros // 1000 # or `/ 1e3` for float
    return posix_timestamp_millis

def encodeURIComponent(s): return urllib.parse.quote(s)



class WebSocketClient():

    def __init__(self):
        pass

    async def connect(self, url:str):
        self.url = url
        self.connection = await websockets.client.connect(url)
        if self.connection.open:
            return self

    async def send_message(self, message):
        await self.connection.send(message)

    async def listen(self, ondata: Callable[..., None], onerror: Callable[..., None] = None, ondone:Callable[...,None] = None, cancel_onerror: bool = False):
        self.ondata = ondata
        self.onerror = onerror
        self.ondone = ondone
        self.paused = False
        self.cancel_onerror = False
        while True:
            try:
                message = await self.connection.recv()
                if not self.paused and self.ondata:
                    try:
                        self.ondata(message)
                    except Exception as err:
                        print(f'{error}')
                        pass
            except websockets.exceptions.ConnectionClosed:
                break
            except Exception as err:
                if self.onerror:
                    self.onerror(err)

                if self.cancel_onerror: 
                    self.connection.close()
                    break
                else: 
                    pass
        if self.ondone: 
            self.ondone()

    async def close(self):
        self.connection.close()

class WCUri:
    def __init__(self, sessiontopic:str=None,bridgeurl:str=None, keyhex:str=None):
        self.sessiontopic = sessiontopic
        self.version = 1
        self.bridgeurl = bridgeurl
        self.keyhex = keyhex

    @classmethod    
    def from_string(cls, wcurl: str):
        match = re.match(r'wc:([^@]+)@(\d+)\?bridge=([^&]+)&key=([0-9a-fA-F]+)',wcurl.strip())
        return cls(sessiontopic=match.group(1),bridgeurl=urllib.parse.unquote_plus(match.group(3)), keyhex=match.group(4))

    def __repr__(self):
        return f'wc:{self.sessiontopic}@{self.version}?bridge={encodeURIComponent(self.bridgeurl)}&key={self.keyhex}'

    def universallink(applink):
        sep = "" if applink.endswith("/") else "/"
        return f'{applink}{sep}wc?uri={encodeURIComponent(self)}'

    def deeplink(applink):
        sep = "//" if applink.endswith(":") else ("" if applink.endswith("/") else "/")
        return f'{applink}{sep}wc?uri={encodeURIComponent(self)}'

class JsonRpc:
    def __init__(self, id:int, method:str = None, params: List[Any] = [], result: Any=None, error:Any=None):
        self.id = id
        self.method = method
        self.params = params
        self.result = result
        self.error = error
        self.jsonrpc = '2.0'

    @classmethod    
    def from_string(cls, json_string: str):
        v = json.loads(json_string)
        return cls(id=v.get('id',None), method = v.get('method',None), params = v.get('params',None), result = v.get('result',None), error = v.get('error',None))

    def __repr__(self):
        if self.method:
            return to_json(dict(id=self.id, jsonrpc=self.jsonrpc, method=self.method, params=self.params or []))
        elif (self.result):
            return to_json(dict(id=self.id, jsonrpc=self.jsonrpc, result=self.result))
        elif (self.error):
            return to_json(dict(id=self.id, jsonrpc=self.jsonrpc, error=self.error))

    def to_json(self):
        return to_json(dict(id=self.id, method=self.method, params=self.params, result = self.result, error = self.error, version = self.version))

class WCPayload:
    def __init__(self, data:str=None, hmac:str=None, iv:str=None):
        self.data = data
        self.hmac = hmac
        self.iv = iv

    @classmethod    
    def from_string(cls, json_string: str):
        v = json.loads(json_string)
        return cls(data=v['data'], hmac = v['hmac'], iv = v['iv'])

    def __repr__(self):
        return self.to_json()

    def to_json(self):
        return to_json(dict(data=self.data, hmac=self.hmac, iv=self.iv))

class WCPubSub:
    def __init__(self, topic:str=None, type:str=None, payload:WCPayload=None, silent:bool=True):
        self.topic = topic
        self.type = type
        self.payload = f'{payload}'
        self.silent = silent

    @classmethod    
    def from_string(cls, json_string: str):
        v = json.loads(json_string)
        return cls(topic=v.get('topic',None), type = v.get('type',None), payloadhex = v.get('payload',None), silent = v.get('silent',None))

    def __repr__(self):
        return self.to_json()

    def to_json(self):
        return to_json(dict(topic=self.topic, type=self.type, payload=self.payload, silent=self.silent))

class WCConnectionRequest:
    def __init__(self, wcuri: WCUri, wsclient: WebSocketClient, sessionrequest: Awaitable):
        self.wcuri = wcuri
        self.wsclient = wsclient
        self.sessionrequest = sessionrequest

class WCSession:
    def __init__(self, bridgeurl: str, sessiontopic: str, ourpeerid: str, keyhex: str, wsclient: WebSocketClient = None, eventhandler: Dict[str, List[Callable]] = None, chainid:int = None):
        self.bridgeurl = bridgeurl
        self.sessiontopic = sessiontopic
        self.ourpeerid = ourpeerid
        self.keyhex = keyhex
        self.wsclient = wsclient
        self.eventhandler = eventhandler
        self.outstandingrpc = {}
        self.theirpeerid = None
        self.theirmeta = None
        self.isactive = False
        self.isconnected = False
        self.theiraccounts = None
        self.theirchainid = None
        self.theirrpcurl = None
        self.chainid = chainid

    async def send_request(self, method: str, params: List[Any], peerid:str=None, timeout:int = None):
        id = jsnow()
        jsonrpc = JsonRpc(id, method=method, params=params);
        ivhex = get_random_bytes(16).hex()
        wcrequest = wcencrypt(to_json(jsonrpc), self.keyhex, ivhex)
        wcrequest_pub:WCPubSub = WCPubSub(peerid or self.theirpeerid, type='pub', payload=wcrequest, silent=True)
        completer = Future()
        response_jsonrpc = JsonRpc(id, result = {'status': 'success'})
        if method != 'wc_sessionUpdate':
            self.outstandingrpc[id] = (jsonrpc, completer)
        message = wcrequest_pub.to_json()
        await self.wsclient.send_message(message);
        return (id, completer)

    async def send_response(self, id:int, method:str, result:Any = None, error: Any = None, peerid: str = None):
        jsonrpc = JsonRpc(id, result=result, error=error)
        ivhex = get_random_bytes(16).hex()
        wcrequest = wcencrypt(to_json(jsonrpc), self.keyhex, ivhex)
        wcrequest_pub = WCPubSub(self.theirpeerid, type='pub', payload=wcrequest, silent=True)
        await self.wsclient.send_message(to_json(wcrequest_pub))
        return (id, True)

    async def send_sessionrequest(self, mymeta:dict, timeout:int = 0):
        wcsession_requestparams = [
            {'peerId': self.ourpeerid, 'peerMeta': mymeta, 'chainId': self.chainid}
        ]
        request = await self.send_request('wc_sessionRequest', wcsession_requestparams,peerid = self.sessiontopic, timeout = timeout)
        (id, complete) = request
        x = await complete
        return x

    async def send_sessionrequestresponse(self, sessionrequest:JsonRpc, mymeta:dict,myname:str = None, accounts:List[str]=None, approved:bool=True, rpcurl:str=None, chainid:int=None,ssl:bool=True):
        wcsession_requestresult = {
            'approved': approved,
            'accounts': accounts,
            'rpcUrl': rpcurl,
            'ssl': ssl,
            'networkId': chainid,
            'peerId': self.ourpeerid,
            'name': myname,
            'peerMeta': mymeta,
            'chainId': chainid
        }
        if approved:
            self.isconnected = True
            self.isactive = True
            wc_requestsub = WCPubSub(self.ourpeerid, type='sub', silent=True)
            await self.wsclient.send_message(to_json(wc_requestsub))
        response = await self.send_response(sessionrequest.id, sessionrequest.method, result=wcsession_requestresult)
        return response

    async def connect(self, sessionrequesthandler:Callable[['WCSession', JsonRpc], None]=None, topic:str=None):
        def ondata(message:str):
            jsonrpc = wcdecode_pubsubmessage(message, self.keyhex)
            method = jsonrpc.method
            result = jsonrpc.result
            error = jsonrpc.error
            if method == 'wc_sessionRequest':
                params = jsonrpc.params
                request = params[0]
                self.theirmeta = request.get('peerMeta',None)
                self.theirpeerid = request.get('peerId',None)
                self.theirchainid = request.get('chainId',None)
                self.theirrpcurl = request.get('rpcUrl', None)
                self.theiraccounts = request.get('accounts',None)
                if sessionrequesthandler:
                    sessionrequesthandler(self, jsonrpc)
            elif method == 'wc_sessionUpdate':
                self.isactive = request.get('approved', self.isactive)
                self.isconnected = self.isactive
                self.theirchainid = request.get('chainId',None)
                self.theirrpcurl = request.get('rpcUrl',None)
                self.theiraccounts = request.get('accounts',None)
            if method != 'wc_sessionRequest' or len((self.eventhandler or {}).get('wc_sessionRequest',[])) > 0:
                process_message(self, jsonrpc)

        def ondone():
            pass

        def onerror(err):
            pass

        wsurl = re.sub(r'^http', 'ws', self.bridgeurl)
        subtopic = topic or self.ourpeerid
        wcsession_sub = WCPubSub(subtopic, type='sub',payload={}, silent=True)
        self.wsclient = wsclient = WebSocketClient()
        await wsclient.connect(wsurl)
        asyncio.create_task(wsclient.listen(ondata=ondata, ondone=ondone, onerror=onerror, cancel_onerror = True))
        await wsclient.send_message(wcsession_sub.to_json())
        return self

    def __repr__(self):
        obj = {
          'ourPeerId': self.ourpeerid,
          'theirPeerId': self.theirpeerid,
          'theirMeta': self.theirmeta,
          'isConnected': self.isconnected,
          'isActive': self.isactive,
          'theirChainId': self.theirchainid,
          'theirRpcUrl': self.theirrpcurl,
          'accounts': self.theiraccounts,
          'bridgeUrl': self.bridgeurl
        };
        return to_json(obj)

    @classmethod    
    async def create_session(cls, bridgeurl:str, mymeta:dict, jsonrpchandler:Dict[str,List[Callable]]=None,chainid:int=None,timeout:int=0) -> WCConnectionRequest:
        sessiontopic = str(uuid.uuid4())
        mypeerid = str(uuid.uuid4())
        keyhex = get_random_bytes(32).hex()
        wcversion = 1;
        wcsession = WCSession(bridgeurl=bridgeurl, sessiontopic=sessiontopic, keyhex=keyhex, ourpeerid=mypeerid,eventhandler=jsonrpchandler)
        wcuri = WCUri(sessiontopic, bridgeurl, keyhex)
        try:
            await wcsession.connect()
            return WCConnectionRequest(wcuri, wcsession.wsclient,wcsession.send_sessionrequest(mymeta,timeout=timeout))
        except Exception as err:
            print(f'failed to create session {wcsession} {err}')

    @classmethod    
    async def connect_session(cls, wcurl:str, mymeta:dict, jsonrpchandler:Dict[str,List[Callable]]=None,chainid:int=None,timeout:int=0):
        def request_retrieved(wcsession: WCSession, jsonrpc: JsonRpc):
            completer.set_result((wcsession, jsonrpc))
        try:
            wcuri = WCUri.from_string(wcurl)
            sessiontopic = wcuri.sessiontopic
            mypeerid = str(uuid.uuid4())
            keyhex = wcuri.keyhex
            bridgeurl = wcuri.bridgeurl
            wcversion = wcuri.version;
            completer = asyncio.Future()
            wcsession = WCSession(bridgeurl=bridgeurl, sessiontopic=sessiontopic, keyhex=keyhex, ourpeerid=mypeerid,eventhandler=jsonrpchandler)
            wcuri = WCUri(sessiontopic, bridgeurl, keyhex)
            await wcsession.connect(sessionrequesthandler=request_retrieved, topic=sessiontopic)
            return completer
        except Exception as err:
            print(f'failed to connect to session {wcurl} {err}')


def wcencrypt(data:str, keyhex:str, ivhex:str) -> WCPayload:
    key = bytearray.fromhex(keyhex)
    iv = bytearray.fromhex(ivhex)
    aes = AES.new(key, AES.MODE_CBC, iv)
    encrypted = aes.encrypt(pad(data.encode('utf-8'), 16))
    tobesigned = encrypted + iv
    signature = hmac.new(key, msg = tobesigned, digestmod = hashlib.sha256).hexdigest()
    return WCPayload(data=encrypted.hex(), hmac = signature, iv=ivhex)

def wcdecrypt(datahex:str, keyhex:str, ivhex:str, datasig: str=None) -> str:
    key = bytearray.fromhex(keyhex)
    iv = bytearray.fromhex(ivhex)
    encrypted = bytearray.fromhex(datahex)
    tobesigned = encrypted + iv
    signature = hmac.new(key, msg = tobesigned, digestmod = hashlib.sha256).hexdigest()
    if datasig and not hmac.compare_digest(datasig,signature): raise Exception('signature not match')

    aes = AES.new(key, AES.MODE_CBC, iv)
    decrypted = aes.decrypt(encrypted)
    unpadded = unpad(decrypted, 16)
    return unpadded.decode("utf-8") 

def wcdecode_pubsubmessage(message:str, keyhex:str) -> JsonRpc:
    pubsub = json.loads(message);
    payload = json.loads(pubsub['payload'])
    wc_request = wcdecrypt(payload['data'], keyhex, payload['iv'], datasig=payload['hmac']);
    jsonrpc = JsonRpc.from_string(wc_request);
    return jsonrpc;

def process_message(wcsession: WCSession, jsonrpc: JsonRpc):
    id = jsonrpc.id;
    method = jsonrpc.method
    params = jsonrpc.params
    result = jsonrpc.result
    error = jsonrpc.error
    internalerr = None
    handled = False
    hashandler = False
    if method:
        if wcsession.eventhandler:
            handlers = wcsession.eventhandler.get(method,[])
            for handler in handlers:
                try: 
                    hashandler = True;
                    handler(wcsession, jsonrpc);
                    handled = True;
                except Exception as err:
                    internalerr = err;

            if not handled and not hashandler and not internalerr:
                handlers = wcsession.eventHandler.get('_',[])
                for handler in handlers:
                    try: 
                        handler(wcsession, jsonrpc);
                        handled = True;
                    except Exception as err:
                        internalerr = err;

            if not handled:
                errorresponse = {
                'id': jsonRpc.id,
                'error': {
                    'code': -32063 if internalerr else -32601,
                    'message':
                        f"{internalerr}" if internalerr else 'method not found'
                    }
                }
                wcsession.sendresponse(jsonrpc.id, jsonrpc.method, error=errorresponse)
    elif result or error:
        request = wcsession.outstandingrpc.pop(id,None)
        if request:
            req, completer = request
            method = req.method
            if method == 'wc_sessionRequest':
                if result:
                    wcsession.theirmeta = result['peerMeta']
                    wcsession.theirpeerid = result['peerId']
                    wcsession.theirchainid = result['chainId']
                    wcsession.theirrpcurl = result['rpcUrl']
                    wcsession.theiraccounts = result['accounts']
                    wcsession.isactive = result['approved']
                    wcsession.isconnected = wcsession.isactive
            if completer:
                if result:
                    completer.set_result((wcsession, jsonrpc, req))
                else:
                    completer.set_exception(Exception(f"{error}"))
