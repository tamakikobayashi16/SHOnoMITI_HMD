# RemoteGame：遠隔のお手本をなぞる

## 新しいシーンの流れ

`InputName → RemotePreview → RemoteGame`。RemotePreviewで筆跡をリアルタイム表示し、`experimentEnd`受信後に約1秒間完成を表示して自動遷移する。筆跡の座標をGlobalData.RemoteTemplateにコピーし、RemoteGameで読み込む。前段シーンが破棄されても筆跡は残り、RemoteGameは再受信しない。

現在はRemotePreviewの`Use Local Test Template`が有効。テスト文字「十」を横画・縦画の順に少しずつ描き、完成後にRemoteGameへ進む。shodoVR受信に戻す際は**RemotePreviewManagerのこのチェック**を外す。前段から渡されるテスト／実受信の情報をRemoteGameが使うため、実受信時は通常どおりボードの準備を行い、テスト時だけ省略する。

shodoVR側の完成ボタン取り込み用コードと説明は`Integration/shodoVR`に用意した。別PCへの取り込みはまだ行っていない。

## 動作

RemoteGameを直接再生した場合は、Managerの`Use Local Test Template`で即時表示のテストに戻れる。InputNameから進む通常の流れではRemotePreviewの設定と受信データを使う。

1. InputNameの開始ボタン／EnterでRemotePreviewへ進む。
2. 遠隔者がshodoVRのPenTabletでお手本を書く。RemotePreviewの中央に随時表示し、右側に文字ごとに保持する。
3. 遠隔者がEnterまたは追加の完成ボタンで通知する。完成した筆跡を引き継ぎ、自動でRemoteGameへ進む。
4. 実受信モードではバランスボードの接続・キャリブレーション後にカウントダウンを始める。テストモードではボードの準備を省略する。
5. 1文字目の筆跡を灰色で大きく表示する。バランスボードで移動し、WM-1／WM-3のBまたはSpaceで書く。
6. WM-2／WM-4のB、EnterまたはTabで保存し、次の文字を表示する。RemoteGameでは時間制限を無効にしている。
7. 全文字を保存したら、既存の完了表示と結果画面の選択へ進む。

テスト中は`Skip Balance Board In Local Test`も有効にしているため、バランスボードの接続・キャリブレーション・自動再接続を省略し、カウントダウンへ進む。このチェックを外すとローカルテストでもボードの準備を行う。`Use Local Test Template`を外した通常のUDP受信モードでは、必ず従来のボード準備を行う。ボード省略中のカーソル移動は追加していない。

## shodoVR側の設定と操作

対象コード：[DrawingUdpSender.cs](https://github.com/tamakikobayashi16/shodoVR/blob/HEAD/Assets/DrawingUdpSender.cs)、[PenPosi.cs](https://github.com/tamakikobayashi16/shodoVR/blob/HEAD/Assets/PenPosi.cs)。

- PenPosiの`udpSender`にDrawingUdpSenderを割り当てる。
- DrawingUdpSenderの`receiverIp`を体験者PCのIPv4アドレスにする。別PCの場合、`127.0.0.1`は使わない。
- `receiverPort`は`5005`。設定してから再生する。
- 入力したお題と同じ文字数のお手本を書く。1文字書くごとにSpaceで次の文字へ進む。書き直しはR。最後はEnterで単語全体の完成を送る。
- 1回の受信で使用する送信セッションは1つ。再挑戦時は両方のシーン／再生を再起動する。

InputNameが空の場合は、受信した文字数を使う。文字そのものは通信データに含まれないため、結果画面にお題を表示したい場合はInputNameに入力する。

## 通信

shodoVRのJSON形式を受信する（既存HMDReceiverの50001番CSVとは別）。

```json
{"version":1,"session":"session-id","method":"PenTablet","eventType":"point","sequence":0,"character":0,"stroke":0,"x":0.1,"y":0.2,"z":10,"pressure":0.5}
```

`point`で追加、`clearCharacter`で該当文字だけ消去、`nextCharacter`では前の文字を保持、`experimentEnd`でお手本を固定する。`strokeEnd`の通し番号も処理して順序を管理する。順序の入れ替わりは150ms待って並べ直し、欠落が残ったらメッセージを表示してなぞりの開始を止める。

座標はPenPosiの標準範囲（X ±1.92、Y ±1.08）を紙へ等比変換する。送信側の`drawScale`を変えた場合は、RemoteGameのManagerにある`remoteSourceHalfSize`を同じ倍率で調整する。

こちらから遠隔側への「お手本開始」送信は今回の実装には含めていない。後で開始メッセージとshodoVR側の待機・受信処理を追加する。

## 検証

既存のUnity参照を使いプロジェクト内のC#をコンパイル済み。受信スクリプトを隔離したUnity 2021.3.4f1で、実UDP受信、文字の保持、書き直し、重複除外、順序の回復、座標変換、完成後の固定、ソケット再起動、欠落検出、データ引き継ぎ、テスト筆跡の逐次再生の23項目を確認した。RemotePreviewのシーン読み込みからテスト筆跡の完成・RemoteGameへの自動遷移・66座標の保持までを、隔離プロジェクトの簡易RemoteGameシーンで確認した。

テストコードは`Tests/RemoteHandwriting`、結果は`verification.txt`。再実行は必ず別のUnityプロジェクトで行う。受信スクリプト、RemoteTemplateDataとテストをコピーし、テストをAssets/Editorに配置して`-executeMethod RemoteSmoke.Run`で起動する。標準のjsonserializeとphysicsモジュールが必要。シーン遷移テストのPreviewFlowSmokeは検証用RemoteGameを作成するため、TempRemoteVerificationという隔離プロジェクトでのみ実行できる。

実際の2台のPC、ペンタブ、バランスボード、Wiiリモコンを組み合わせた表示・操作確認は未実施。
