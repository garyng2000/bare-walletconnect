import 'dart:html';
import 'walletconnect.dart';

void main() {
  querySelector('#output').text = 'Your Dart app is running.';
  querySelector('#connectDApp').onClick.listen((event) async {
    print('wallet connect invoked');
    var wcUri = (querySelector('#wcUri') as InputElement).value;
    var sessionRequest = await wcConnectDApp(wcUri, jsonRpcHandler: {
      '_': [echo_handler]
    });
    var wcSession = sessionRequest.item1;
    var request = sessionRequest.item2;
    // ignore: omit_local_variable_types
    List<String> accounts = [];
    var myMeta = {
      'description': 'Testing DART Wallet',
      'url': 'https://www.google.com',
      'icons': [
        'https://raw.githubusercontent.com/MetaMask/brand-resources/master/SVG/metamask-fox.svg'
      ]
    };
    var result = await wcSession.sendSessionRequestResponse(
        request, 'my test wallet', myMeta, accounts, true);
    print('session request ${result.item1} approved $wcSession');
    var pong = await wcSession.sendRequest('wc_pong', []);
    var id = pong.item1;
    print('wc_pong $id request');
    var requestResult = await pong.item2;
    print('wc_pong $id result $requestResult');
  });
  var bridgeUrl = (querySelector('#wcUri') as InputElement).value;
  querySelector('#connectWallet').onClick.listen((event) async {
    var myMeta = {
      'description': 'Testing DART DApp',
      'url': 'https://www.google.com',
      'icons': [
        'https://raw.githubusercontent.com/MetaMask/brand-resources/master/SVG/metamask-fox.svg'
      ]
    };
    var sessionRequest =
        await wcConnectWallet(bridgeUrl, myMeta, jsonRpcHandler: {
      '_': [echo_handler]
    });
    print(sessionRequest.wcUri);
    (querySelector('#wcUri') as InputElement).value =
        sessionRequest.wcUri.toString();
    var wcSession = await sessionRequest.wcSessionRequest;
    print('session request replied $wcSession');
    if (wcSession.isConnected) {
      var ping = await wcSession.sendRequest('wc_ping', []);
      var id = ping.item1;
      print('wc_ping $id request');
      var requestResult = await ping.item2;
      print('wc_ping $id result $requestResult');
    }
  });
}
