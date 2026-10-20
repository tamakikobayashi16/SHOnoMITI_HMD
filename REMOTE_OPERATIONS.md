# 遠隔お手本となぞりの操作

## 起動前

両PCを同じLAN・Wi-Fiにつなぐ。体験者側は`codex/remote-template-preview`、shodoVR側は`codex/remote-trial-session`を使う。

1. 体験者PCで`ipconfig`を実行し、使用中の接続のIPv4を確認する。
2. shodoVRのSampleScene → Pentab → Drawing Udp Senderで、Receiver IPに体験者PCのIPv4を入力する。Receiver Portは5005、Control Portは5006、Require Session HandshakeはON。設定してシーンを保存する。
3. 体験者側のRemotePreviewManagerとRemoteGameのManagerのUse Local Test TemplateをOFFにする（現在の保存済み設定）。
   RemoteGameのManagerの`Skip Balance Board In Local Test`もOFF（本番用の保存設定）。
4. ペンタブと、従来のバランスボード・Wiiリモコンの接続環境を準備する。

## お手本を見る

1. shodoVRのSampleSceneを再生する。描画用Planeは開始まで非表示。
2. 体験者側のInputNameを再生し、3文字以内のお題を入力して開始する。
3. RemotePreviewで接続を待つ。接続前の待ち時間は無制限。両側の「準備完了」を押すと、お手本を書けるようになる。
4. 遠隔者がペンタブで書く。体験者側は中央に受信中の文字、右側に1〜3文字目の一覧を表示する。

| 遠隔者の操作 | 内容 |
|---|---|
| ペンタブ | お手本を書く |
| Space | 今の文字を終えて、次の文字へ |
| R | 今の文字を消して書き直す |
| Enter／テンキーEnter | 単語全体を完成させる |

例：「あいう」は、あ → Space → い → Space → う → Space → え → Space → お → Enter。

完成した文字は紙に収まるよう調整される。全文字が完成すると、RemoteGameへ自動で進む。

## なぞる

1. 画面の案内に従ってバランスボードの接続・キャリブレーションを行う。
2. 開始カウントダウン後、1文字目からなぞる。各文字の制限時間は40秒。紙の上の数字が残り時間。

| 体験者の操作 | 内容 |
|---|---|
| バランスボード | 描画位置を動かす |
| WM-1／WM-3のB | 押している間、描画する |
| WM-2／WM-4のB | 保存して次の文字へ |
| キーボードSpace | 描画ボタンの代用 |
| キーボードEnter／テンキーEnter／Tab | 保存して次の文字へ（描画後） |

40秒で途中の筆跡を自動保存し、次の文字へ進む。未入力なら白紙を保存する。余った時間は持ち越さず、次も40秒から始まる。全文字完了後は、完了演出を経て既存の結果選択へ進む。

## やり直し・記録

- RemotePreviewの「やり直す」、またはRemoteGameの「最初からやり直す」で、新しい試行としてお手本から再開する。両側の準備完了をもう一度押す。
- 接続後の通信停止は5秒、お手本入力の停止は既定60秒で検出する。エラー時はIP・ネットワーク・機器を確認してからやり直す。
- 記録は各PCの`Application.persistentDataPath/RemoteTrials/<試行ID>/`へ自動保存。同じ試行IDで対応付ける。詳細はREMOTE_GAME.md参照。
- テストする場合はUse Local Test TemplateをONにする。「あいう」を自動表示する。本番に戻すときはOFF。
