import 'dart:html';
import 'walletconnect.dart';

void main() {
  querySelector('#output').text = 'Your Dart app is running.';
  querySelector('#connectDApp').onClick.listen((event) async {
    print('wallet connect invoked');
    var wcUri = (querySelector('#wcUri') as InputElement).value;
    var sessionRequest = await WCSession.connectSession(wcUri, jsonRpcHandler: {
      '_': [echo_handler]
    });
    var wcSession = sessionRequest.item1;
    var request = sessionRequest.item2;
    // ignore: omit_local_variable_types
    List<String> accounts = ['0x1234567890123456789012345678901234567890'];
    var myMeta = {
      'description': 'Testing DART Wallet',
      'name': 'test dart',
      'url': 'https://www.google.com',
      'icons': [
        'https://raw.githubusercontent.com/MetaMask/brand-resources/master/SVG/metamask-fox.svg'
      ]
    };
    print('session request from $request');
    var result = await wcSession.sendSessionRequestResponse(
        request, 'my test wallet', myMeta, accounts, true,
        chainId: 1, ssl: true, rpcUrl: 'https://infura.io');
    print('session request ${result.item1} approved $wcSession');
    await Future.delayed(Duration(seconds: 10), () {});
    var pong = await wcSession.sendRequest('wc_pong', []);
    var id = pong.item1;
    print('wc_pong $id request');
    var requestResult = await pong.item2;
    print('wc_pong $id result $requestResult');
  });
  querySelector('#connectWallet').onClick.listen((event) async {
    var iosWalletRegistry = await getWCWalletRegistry();
    var androidWalletRegistry = await getWCWalletRegistry(ios: false);
    iosWalletRegistry.forEach((w) {
      print('${w.name} - ${w.iosDeepLink}');
    });
    var bridgeUrl = (querySelector('#bridgeUrl') as InputElement).value;
    var myMeta = {
      'description': 'Testing DART DApp',
      'name': 'test dart',
      'url': 'https://www.blabla.com/',
      'icons': ['https://blabla.com/favicon.png']
    };
    try {
      var sessionRequest = await WCSession.createSession(bridgeUrl, myMeta,
          jsonRpcHandler: {
            '_': [echo_handler]
          },
          chainId: 1);
      print(sessionRequest.wcUri);
      (querySelector('#wcUri') as InputElement).value =
          sessionRequest.wcUri.toString();
      (querySelector('#deepLink') as InputElement).value =
//          sessionRequest.wcUri.universalLink('https://rnbwapp.com/');

          sessionRequest.wcUri.universalLink('https://metamask.app.link/');
      sessionRequest.wcUri.toString();
      var wcSession = await sessionRequest.wcSessionRequest;
      print('session request replied $wcSession');
      await wcSession.close();
      await Future.delayed(Duration(seconds: 60), () {});
      await wcSession.connect();
      print('reconnect');
      var params = [];
      var x = await wcSession.sendRequest('eth_accounts', params);
      print('eth_accounts sent $x');
      var y = await x.item2;
      print('eth_accounts result $y');
      // if (wcSession.isActive) {
      //   var ping = await wcSession.sendRequest('wc_ping', []);
      //   var id = ping.item1;
      //   print('wc_ping $id request');
      //   var requestResult = await ping.item2;
      //   print('wc_ping $id result $requestResult');
      // }
    } on WCCustomException catch (error) {
      print('session request error ${error.error}');
    } catch (error) {
      print('session request error $error');
    }
  });
}
