open System
open System.Linq
open System.IO
open System.Text
open System.Text.RegularExpressions
open System.Runtime.Serialization
open System.Xml
open System.Threading
open System.Threading.Tasks
open CoreTweet
open CoreTweet.Core

type AsyncBuilder with
  member x.Bind(t:Task<'T>, f:'T -> Async<'R>) : Async<'R> = 
    async.Bind(Async.AwaitTask t, f)

let getTokens () =
  let unix = (Environment.OSVersion.Platform = PlatformID.Unix || Environment.OSVersion.Platform = PlatformID.MacOSX)
  let tf = if unix then ".twtokens" else "twtokens.xml"
  let home = if unix then Environment.GetEnvironmentVariable ("HOME") else Path.Combine (Environment.GetFolderPath (Environment.SpecialFolder.ApplicationData), "csharp")
  let tpath = Path.Combine (home, tf)
  Directory.CreateDirectory home |> ignore
  let x = new DataContractSerializer (typeof<string[]>)

  if (File.Exists tpath) then
    use y = XmlReader.Create tpath in
      let ss = x.ReadObject y :?> string [] in
      Tokens.Create(ss.[0], ss.[1], ss.[2], ss.[3])
  else
    Console.Write "Consumer key> "
    let ck = Console.ReadLine ()
    Console.Write "Consumer secret> "
    let cs = Console.ReadLine ()
    async {
      let! se = OAuth.AuthorizeAsync (ck, cs) in
      Console.WriteLine ("Open: " + se.AuthorizeUri.ToString ())
      Console.Write "PIN> "
      let! g = se.GetTokensAsync (Console.ReadLine()) |> Async.AwaitTask in
      let s = XmlWriterSettings () in
      do s.Encoding <- System.Text.UTF8Encoding false
      use y = XmlWriter.Create (tpath, s) in
        x.WriteObject (y, [| g.ConsumerKey; g.ConsumerSecret; g.AccessToken; g.AccessTokenSecret |])
      return g
    } |> Async.RunSynchronously

[<EntryPoint>]
let main argv =
  let tokens = getTokens ()
  let awaitRateLimit (response: #CoreTweet.Core.ITwitterResponse)=
    async {
      if isNull response.RateLimit |> not && response.RateLimit.Remaining = 0 then
        do printfn "[!] ratelimit reached! waiting until %A..." response.RateLimit.Reset
        do! Async.Sleep (response.RateLimit.Reset - DateTimeOffset.UtcNow).Milliseconds
        do! Async.Sleep 2000
    }

  async {
    let! self = tokens.Account.VerifyCredentialsAsync()
    
    let rec getFriends prev cursor =
      async {
        let! response =
          tokens.Friends.ListAsync(
            user_id=self.Id.Value,
            cursor=Nullable cursor,
            count=Nullable 200
          )
        do! awaitRateLimit response
        do! Async.Sleep 100
        if response.NextCursor = 0L then
          return response.Result |> Seq.ofArray |> Seq.append prev
        else
          return! getFriends (Seq.append prev (Seq.ofArray response.Result)) response.NextCursor
      }
    do printfn "fetching all the friends..."
    let! friends = getFriends Seq.empty -1L
    let friends = Array.ofSeq friends
    do printfn "done. (%i friends total)" (Array.length friends)

    do printfn "creating a new list..."
    let! newList = tokens.Lists.CreateAsync(sprintf "F%A" DateTime.Now, "private")
    do printfn "done. (%s)" newList.Uri

    do printfn "adding friends to the list..."
    for friendsChunk, i in friends |> Array.map (fun x -> x.Id.Value)
                                   |> Array.chunkBySize 100
                                   |> Array.mapi (fun i x -> x,i) do
      printfn "- %i/%i" (100*(i+1)) friends.Length
      let! response =
        tokens.Lists.Members.CreateAllAsync(newList.Id, friendsChunk)
      do! awaitRateLimit response
      do! Async.Sleep 100
    do printfn "done!"      

  } |> Async.RunSynchronously
  0
