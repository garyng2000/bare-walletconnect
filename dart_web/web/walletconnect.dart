import 'dart:async';
import 'dart:convert';
import 'package:convert/convert.dart';
import 'package:encrypt/encrypt.dart';
import 'package:crypto/crypto.dart';
import 'package:tuple/tuple.dart';
import 'package:uuid/uuid.dart';
import 'package:web_socket_channel/web_socket_channel.dart';

//import 'package:web_socket_channel/status.dart' as status;

class WCUri {
  WCUri(this.topic, this.version, this.bridgeUrl, this.keyHex);

  String topic;
  int version;
  String bridgeUrl;
  String keyHex;

  @override
  String toString() {
    var encodedBridgeUrl = Uri.encodeComponent(bridgeUrl);
    return 'wc:$topic@$version?bridge=$encodedBridgeUrl&key=$keyHex';
  }

  static WCUri fromString(String wcUrl) {
    var rx = RegExp(r'wc:([^@]+)@(\d+)\?bridge=([^&]+)&key=([0-9a-fA-F]+)');
    var match = rx.firstMatch(wcUrl.trim());
    if (match != null) {
      return WCUri(match.group(1), int.parse(match.group(2)),
          Uri.decodeFull(match.group(3)), match.group(4));
    } else {
      throw ('malformed walletconnect uri');
    }
  }
}

class JsonRpc {
  JsonRpc(this.id, {this.method, this.params, this.result, this.error});

  int id;
  String jsonrpc = '2.0';
  String method;
  List<dynamic> params;
  Map<String, dynamic> result;
  Map<String, dynamic> error;
  Completer completer;
  factory JsonRpc.fromJson(Map<String, dynamic> jsonRpcObj) {
    return JsonRpc(jsonRpcObj['id'],
        method: jsonRpcObj['method'],
        result: jsonRpcObj['result'],
        error: jsonRpcObj['error'],
        params: jsonRpcObj['params']);
  }

  Map<String, dynamic> toJson() => {
        'id': id,
        'jsonrpc': jsonrpc,
        'method': method,
        'params': params,
        'result': result,
        'error': error
      };

  @override
  String toString() {
    return jsonEncode(this);
  }
}

class WCPubSub {
  WCPubSub({this.topic, this.type, this.payload, this.silent});

  String topic;
  String type;
  String payload;
  bool silent;
  WCPubSub.fromJson(Map<String, dynamic> json)
      : topic = json['topic'],
        type = json['type'],
        payload = json['payload'],
        silent = json['silent'];

  Map<String, dynamic> toJson() =>
      {'topic': topic, 'type': type, 'payload': payload, 'silent': silent};
}

class WCPayload {
  WCPayload({this.data, this.hmac, this.iv});

  String data;
  String hmac;
  String iv;

  WCPayload.fromJson(Map<String, dynamic> json)
      : data = json['data'],
        hmac = json['hmac'],
        iv = json['iv'];

  Map<String, dynamic> toJson() => {'data': data, 'hmac': hmac, 'iv': iv};
}

WCPubSub wcPubsub(topicId, payload, type, isSilent) {
  return WCPubSub(
      topic: topicId,
      payload: jsonEncode(payload),
      type: type,
      silent: isSilent);
}

class WCSession {
  WCSession(
      {this.sessionTopic,
      this.webSocketChannel,
      this.keyHex,
      this.ourPeerId,
      this.eventHandler,
      this.bridgeUrl});

  WebSocketChannel webSocketChannel;
  StreamSubscription<dynamic> streamSubscription;
  String bridgeUrl;
  String sessionTopic;
  String keyHex;
  String ourPeerId;
  bool isConnected = false;
  Map<int, Tuple2<JsonRpc, Completer<dynamic>>> outstandingRpc = {};
  Map<String, List<JsonRpc Function(WCSession, JsonRpc)>> eventHandler = {};
  int chainId;
  String rpcUrl;
  String theirPeerId;
  Map<String, dynamic> theirMeta;

  Future<Tuple2<int, Future<dynamic>>> sendRequest(method, params) async {
    if (!isConnected) {
      return Future.error('invalid session');
    }
    var id = DateTime.now().millisecondsSinceEpoch;
    var jsonRpc = JsonRpc(id, method: method, params: params);
    var ivHex = IV.fromSecureRandom(16).base16;
    var wcRequest = wcEncrypt(jsonEncode(jsonRpc), keyHex, ivHex);
    var wcRequestPub = wcPubsub(theirPeerId, wcRequest, 'pub', true);
    var completer = Completer<dynamic>();
    outstandingRpc[id] = Tuple2(jsonRpc, completer);
    webSocketChannel.sink.add(jsonEncode(wcRequestPub));
    return Tuple2(id, completer.future);
  }

  Future<Tuple2<int, Future<dynamic>>> sendResponse(id, method,
      {result, error}) async {
    if (!isConnected && method != 'wc_sessionRequest') {
      return Future.error('invalid session');
    }
    var jsonRpc = JsonRpc(id, result: result, error: error);
    var ivHex = IV.fromSecureRandom(16).base16;
    var wcRequest = wcEncrypt(jsonEncode(jsonRpc), keyHex, ivHex);
    var wcRequestPub = wcPubsub(theirPeerId, wcRequest, 'pub', true);
    webSocketChannel.sink.add(jsonEncode(wcRequestPub));
    if (method == 'wc_sessionRequest') {
      var wcRequestSub = wcPubsub(ourPeerId, {}, 'sub', true);
      webSocketChannel.sink.add(jsonEncode(wcRequestSub));
    }
    return Tuple2(id, Future.value(true));
  }

  Future<Tuple2<int, Future>> sendSessionRequestResponse(
      JsonRpc sessionRequest,
      String myName,
      Map<String, dynamic> myMeta,
      List<String> accounts,
      bool approved,
      {String rpcUrl,
      bool ssl = true}) {
    var wcSessionRequestResult = {
      'approved': approved,
      'accounts': accounts,
      'rpcUrl': rpcUrl,
      'ssl': ssl,
      'networkId': chainId,
      'peerId': ourPeerId,
      'name': myName,
      'peerMeta': myMeta,
      'chainId': chainId
    };
    var response = sendResponse(sessionRequest.id, sessionRequest.method,
        result: wcSessionRequestResult);
    isConnected = true;
    return response;
  }

  void setEventHandler(method, handler, {remove = false}) {
    eventHandler ??= {};
    if (!eventHandler.containsKey(method)) {
      eventHandler[method] = [];
    }
    var handlers = eventHandler[method];
    if (!remove) {
      handlers.contains(handler) ?? handlers.add(handler);
    } else {
      handlers.contains(handler) ?? handlers.remove(handler);
    }
  }

  @override
  String toString() {
    var obj = {
      'ourPeerId': ourPeerId,
      'theirPeerId': theirPeerId,
      'theirMeta': theirMeta,
      'isConnected': isConnected,
      'bridgeUrl': bridgeUrl
    };
    return jsonEncode(obj);
  }
}

class WCConnectionRequest {
  WCConnectionRequest(
      {this.wcUri,
      this.webSocketChannel,
      this.streamSubscription,
      this.wcSessionRequest});

  WebSocketChannel webSocketChannel;
  StreamSubscription<dynamic> streamSubscription;
  Future<WCSession> wcSessionRequest;
  WCUri wcUri;
}

WCPayload wcEncrypt(data, keyHex, ivHex) {
  var iv = IV.fromBase16(ivHex);
  var key = Key.fromBase16(keyHex);
  final encrypter = Encrypter(AES(key, mode: AESMode.cbc));
  final encrypted = encrypter.encrypt(data, iv: iv);
  var hmac = Hmac(sha256, hex.decode(keyHex)); // HMAC-SHA256
  var toBeSigned = encrypted.bytes + iv.bytes;
  var sig = hmac.convert(toBeSigned);
  return WCPayload(data: encrypted.base16, hmac: sig.toString(), iv: ivHex);
}

String wcDecrypt(dataHex, keyHex, ivHex) {
  var iv = IV.fromBase16(ivHex);
  var key = Key.fromBase16(keyHex);
  final encrypter = Encrypter(AES(key, mode: AESMode.cbc));
  final decrypted = encrypter.decrypt16(dataHex, iv: iv);
  return decrypted;
}

JsonRpc wcDecodePubSubMessage(message, keyHex) {
  var pubsub = jsonDecode(message);
  print(pubsub);
  var payload = jsonDecode(pubsub['payload']);
  print(payload);
  var wc_request = wcDecrypt(payload['data'], keyHex, payload['iv']);
  print(wc_request);
  var jsonRpc = JsonRpc.fromJson(jsonDecode(wc_request));
  return jsonRpc;
}

void processRequest(WCSession wcSession, JsonRpc jsonRpc) {
  var id = jsonRpc.id;
  var method = jsonRpc.method;
  //var params = jsonRpc.params;
  var result = jsonRpc.result;
  var error = jsonRpc.error;
  var internalErr;
  var handled = false;
  var hasHandler = false;
  if (method != null) {
    if (wcSession.eventHandler != null) {
      var handlers = wcSession.eventHandler.containsKey(method)
          ? wcSession.eventHandler[method]
          : [];
      for (var handler in handlers) {
        try {
          hasHandler = true;
          handler(wcSession, jsonRpc);
          handled = true;
        } catch (err) {
          internalErr = err;
          print('$err');
        }
      }
    }
    if (!handled && !hasHandler && wcSession.eventHandler.containsKey('_')) {
      var handlers = wcSession.eventHandler['_'];
      for (var handler in handlers) {
        try {
          handler(wcSession, jsonRpc);
          handled = true;
        } catch (err) {
          internalErr = err;
          print('$err');
        }
      }
    }
    if (!handled) {
      var errorResponse = {
        'id': jsonRpc.id,
        'error': {
          'code': internalErr != null ? -32063 : -32601,
          'message':
              internalErr != null ? internalErr.toString() : 'Method not found'
        }
      };
      wcSession.sendResponse(jsonRpc.id, jsonRpc.method, error: errorResponse);
    }
  } else if (result != null || error != null) {
    if (wcSession.outstandingRpc.containsKey(id)) {
      var request = wcSession.outstandingRpc[id].item1;
      var completer = wcSession.outstandingRpc[id].item2;
      wcSession.outstandingRpc.remove(id);
      if (completer != null) {
        completer.complete(Tuple2(request, jsonRpc));
      }
    } else {
      print('no matching request for response $jsonRpc');
    }
  } else {
    print('uknown request $method');
  }
}

Future<WCConnectionRequest> wcConnectWallet(
    String bridgeUrl, Map<String, dynamic> myMeta,
    {Map<String, List<JsonRpc Function(WCSession, JsonRpc)>> jsonRpcHandler,
    int chainId}) async {
  var uuidGenerator = Uuid();
  var sessionTopic = uuidGenerator.v4();
  var myPeerId = uuidGenerator.v4();
  if (bridgeUrl.trim() != '') {
    bridgeUrl = bridgeUrl.trim();
  } else {
    bridgeUrl = 'https://wcbridge.garyng.com';
  }
  var wsUrl = bridgeUrl.replaceFirst(RegExp(r'^http'), 'ws');
  var keyHex = Key.fromSecureRandom(32).base16;
  var ivHex = IV.fromSecureRandom(16).base16;
  var wcUri = WCUri(sessionTopic, 1, bridgeUrl, keyHex);
  var wcSessionRequestId = DateTime.now().millisecondsSinceEpoch;
  var wcSessionRequestAnswered = false;
  var wcSessionRequestParams = [
    {'peerId': myPeerId, 'peerMeta': myMeta, 'chainId': chainId}
  ];
  var wcSessionRequest = JsonRpc(wcSessionRequestId,
      method: 'wc_sessionRequest', params: wcSessionRequestParams);
  var pubPayLoad = wcEncrypt(jsonEncode(wcSessionRequest), keyHex, ivHex);
  var wcSessionPub = wcPubsub(sessionTopic, pubPayLoad, 'pub', true);
  var wcSessionSub = wcPubsub(myPeerId, {}, 'sub', true);
  var channel = WebSocketChannel.connect(Uri.parse(wsUrl));
  var wcSession = WCSession(
      keyHex: keyHex,
      webSocketChannel: channel,
      ourPeerId: myPeerId,
      bridgeUrl: bridgeUrl.trim(),
      eventHandler: jsonRpcHandler);
  var sessionRequestCompleter = Completer<WCSession>();
  var streamSubscription = channel.stream.listen((message) {
    try {
      var jsonRpc = wcDecodePubSubMessage(message, keyHex);
      if (jsonRpc.id == wcSessionRequestId && !wcSessionRequestAnswered) {
        var result = jsonRpc.result;
        var error = jsonRpc.error;
        if (result != null) {
          wcSession.theirMeta = result['peerMeta'];
          wcSession.theirPeerId = result['peerId'];
          wcSession.isConnected = result['approved'];
        } else if (error != null) {}
        wcSessionRequestAnswered = true;
        sessionRequestCompleter.complete(wcSession);
      } else {
        processRequest(wcSession, jsonRpc);
      }
    } catch (err) {
      print('bad wcconnect request $err');
    }
  }, onError: (err, stack) {
    print('$bridgeUrl $err $stack');
  }, onDone: () {
    print('$bridgeUrl done');
  });
  wcSession.streamSubscription = streamSubscription;
  channel.sink.add(jsonEncode(wcSessionPub));
  channel.sink.add(jsonEncode(wcSessionSub));
  return WCConnectionRequest(
      wcUri: wcUri,
      webSocketChannel: channel,
      streamSubscription: streamSubscription,
      wcSessionRequest: sessionRequestCompleter.future);
}

Future<Tuple2<WCSession, JsonRpc>> wcConnectDApp(String wcUrl,
    {Map<String, List<JsonRpc Function(WCSession, JsonRpc)>>
        jsonRpcHandler}) async {
  var wcUri = WCUri.fromString(wcUrl);
  var bridgeUrl = wcUri.bridgeUrl.trim();
  var wsUrl = bridgeUrl.replaceFirst(RegExp(r'^http'), 'ws');
  var keyHex = wcUri.keyHex;
  var sessionTopic = wcUri.topic;
  var channel = WebSocketChannel.connect(Uri.parse(wsUrl));
  var uuidGenerator = Uuid();
  var myPeerId = uuidGenerator.v4();
  var wcSession = WCSession(
      keyHex: keyHex,
      webSocketChannel: channel,
      ourPeerId: myPeerId,
      bridgeUrl: bridgeUrl,
      eventHandler: jsonRpcHandler);
  var sessionRequestCompleter = Completer<Tuple2<WCSession, JsonRpc>>();
  var streamSubscription = channel.stream.listen((message) {
    var jsonRpc = wcDecodePubSubMessage(message, keyHex);
    var method = jsonRpc.method;
    var params = jsonRpc.params;
    if (method == 'wc_sessionRequest') {
      var request = params[0];
      wcSession.theirMeta = request['peerMeta'];
      wcSession.theirPeerId = request['peerId'];
      sessionRequestCompleter.complete(Tuple2(wcSession, jsonRpc));
    } else {
      processRequest(wcSession, jsonRpc);
    }
  }, onError: (err, stack) {
    print(err);
  }, onDone: () {
    print('done');
  });
  wcSession.streamSubscription = streamSubscription;
  var wc_getRequest = wcPubsub(sessionTopic, {}, 'sub', true);
  channel.sink.add(jsonEncode(wc_getRequest));
  return sessionRequestCompleter.future;
}

JsonRpc echo_handler(WCSession wcSession, JsonRpc jsonRpc) {
  var method = jsonRpc.method;
  var result = jsonRpc.result;
  var error = jsonRpc.error;
  var id = jsonRpc.id;
  if (method != null) {
    var echoResult = {
      'request': {'params': jsonRpc.params, 'method': method, 'id': id}
    };
    wcSession.sendResponse(id, method, result: echoResult);
    return JsonRpc(id, result: echoResult);
  } else if (result != null) {
    print('should not be here, $result');
  } else if (error != null) {
    print('should not be here either, $error');
  }
  return JsonRpc(id, result: {});
}
