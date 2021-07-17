import 'package:http/http.dart';
import 'package:web3dart/crypto.dart';
import 'package:web3dart/web3dart.dart';
import 'package:tuple/tuple.dart';
//import 'package:web_socket_channel/io.dart';
import 'erc721.dart';

Tuple2<String, String> mintTokenTx(String mintTo, String tokenUri) {
  var contractAbi = ContractAbi.fromJson(erc721Abi, 'L1ERC721');
  var contractAddress = EthereumAddress.fromHex(erc721Address);
  var contract = DeployedContract(contractAbi, contractAddress);
  var mintFunction = contract.function('mint');
  var hash = bytesToHex(keccakUtf8(tokenUri));
  print(hash);
  var params = [EthereumAddress.fromHex(mintTo), tokenUri, hexToInt(hash)];
  var txData = bytesToHex(mintFunction.encodeCall(params),
      include0x: true, padToEvenLength: true);

  return Tuple2(erc721Address, txData);
}

dynamic getSymbol(String rpcUrl) async {
  var contractAbi = ContractAbi.fromJson(erc721Abi, 'L1ERC721');
  var contractAddress = EthereumAddress.fromHex(erc721Address);
  var contract = DeployedContract(contractAbi, contractAddress);
  var client = Web3Client(rpcUrl, Client());
  var function = contract.function('symbol');
  var result =
      await client.call(contract: contract, function: function, params: []);
  return result;
}

dynamic getTokenId(
    String rpcUrl, String? walletAddress, String tokenUri) async {
  var contractAbi = ContractAbi.fromJson(erc721Abi, 'L1ERC721');
  var contractAddress = EthereumAddress.fromHex(erc721Address);
  var contract = DeployedContract(contractAbi, contractAddress);
  var client = Web3Client(rpcUrl, Client());
  var event = contract.event('Minted');
  var hash = bytesToHex(keccakUtf8(tokenUri), include0x: true);
  var filter = FilterOptions(
      address: contractAddress,
      fromBlock: BlockNum.genesis(),
      toBlock: BlockNum.current(),
      topics: [
        [
          bytesToHex(event.signature, padToEvenLength: true, include0x: true),
        ],
        [],
        [hash],
        []
      ]);
  var result = await client.getLogs(filter);
  BigInt? tokenId;
  result.forEach((element) {
    tokenId = hexToInt(element.topics![1]);
    print('tokenId $tokenId');
  });
  return tokenId;
}
