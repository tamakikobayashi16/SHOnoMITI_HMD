# shodoVR 遠隔お手本モード

送信側は tamakikobayashi16/shodoVR の main の DrawingUdpSender / PenPosi を使用する。
送信側のコード変更は不要。タイトルの追加ボタンは暫定的な英語のGUI。

## 操作

1. SHOnoMITI_HMD を Unity 2021.3.4f1 で開き、Title シーンを再生する。
2. タイトル演出後、Random practice または Remote example (UDP) を選ぶ。
   Random practice は名前入力を通らず既存のランダムお題で開始する。
3. 遠隔モードでは PreGame シーンで UDP 5005 を待ち受ける。
4. shodoVR の DrawingUdpSender.receiverIp を受信PCの到達可能なIPに、receiverPort を5005にする。
   別PCでは127.0.0.1は使えない。OSのファイアウォールでもUDP 5005の受信を許可する。
5. PenPosi.udpSender にその送信コンポーネントを割り当てる。
6. 遠隔者が描くと現在の文字がリアルタイム表示される。Spaceで次の文字、Rで現在の文字を消去。
   前の文字のデータは保存される。Enterで experimentEnd を送る。
7. 受信側で Start tracing を押す。既存のバランスボード接続・キャリブレーション後に一文字ずつなぞる。
   Spaceまたは既存のWii入力で描き、Enter/Tabまたは既存の保存入力で次の文字へ進む。
8. 全文字終了後は既存の1〜4キーによる結果選択を使う。自動採点は追加していない。

## プロトコル

version=1、method=PenTablet。sequenceはsession全体で0からの連番。
nextCharacter は切り替え後の character を送る。
clearCharacter は現在の文字を消去し、送信側の stroke は0に戻る。
experimentEnd が全文完成。受信後は別sessionを含めてお手本の変更を受け付けない。
点の筆圧は送信されるが、送信側と同様に線幅は一定（0.3）。

点の範囲は送信側のdrawScale=1でxがおよそ±1.92、yが±1.08、z=9.5。
現在は縦横比・大きさを維持して受信側のmoveAreaの中心へ平行移動する。
送信側drawScaleや紙の向きが違う構成では座標変換の追加が必要。

## 欠落時

欠番、順序の逆転による欠番、受信キュー超過を検出した作品では開始ボタンを無効化する。
重複や既に処理した番号は捨てる。自動再送・並べ替えは未実装。
Reset後、送信コンポーネントを無効→有効にしてsession/sequenceを更新し、全文を描き直す。
実験をやり直す場合はPenPosi側のexperimentEndedもリセットされるようシーンを再開始する。
最後のexperimentEnd自体が失われた場合も再開始が必要。

## 検証

Unityメニュー Tools > Remote Ink > Run protocol checks でJSONイベントの再生を検証する。
前文字の保持、文字単位の消去、連続受信の完成、完成後の固定、欠番検出、不正JSONの6チェック。
続いて2文字を送信し、途中でRによる消去を試し、Enterで完成、なぞり、結果画面まで確認する。
クラウドで実行した検証：

```sh
dotnet run --project Tests/RemoteInkHarness/RemoteInkHarness.csproj --configuration Release
```

.NET 8ハーネスで受信コードと遠隔モード管理コードをコンパイルし、上記6チェックと実際のループバックUDP受信、座標保持・移動、リセット後の旧session拒否、ポート解放、ポート競合のエラー表示を確認。
Unity APIには最小限の代替実装を使用するため、Unityの描画、JsonUtility固有動作、既存PreGame全体のコンパイルやゲーム進行を保証しない。
Unity Editorと利用可能なライセンスがないため、Unity上の検証と実機確認は未実施。

遠隔モードでは文字認識結果がないため、既存の文字数進行用に空白のお題を内部生成する。結果画面で認識文字は表示しない。
