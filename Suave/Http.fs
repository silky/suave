﻿namespace Suave
module Http =

  open System
  open System.IO
  open System.Text
  open System.Text.RegularExpressions
  open System.Threading.Tasks

  open Utils
  open Types

  // literals

  let SUAVE_VERSION = Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString()

  let [<Literal>] HTTP_VERSION = "HTTP/1.1"

  // general response functions

  let response_f status_code reason_phrase (f_content : HttpRequest -> Async<unit>) (request : HttpRequest) = async {
    try
      let stream:Stream = request.Stream

      do! async_writeln stream (sprintf "%s %d %s" HTTP_VERSION status_code reason_phrase)
      do! async_writeln stream (sprintf "Server: Suave/%s (http://suave.io)" SUAVE_VERSION)
      do! async_writeln stream (sprintf "Date: %s" (DateTime.UtcNow.ToString("R")))

      for (x,y) in request.Response.Headers do
        if not (List.exists (fun y -> x.ToLower().Equals(y)) ["server";"date";"content-length"]) then
          do! async_writeln stream (sprintf "%s: %s" x y )

      if not(request.Response.Headers.Exists(new Predicate<_>(fun (x,_) -> x.ToLower().Equals("content-type")))) then
        do! async_writeln stream (sprintf "Content-Type: %s" "text/html")

      do! f_content request

    with //the connection might drop while we are sending the response
    | :? IOException as ex  -> raise (InternalFailure "Failure while writing to client stream")
    }

  let response status_code reason_phrase (content : byte []) (request : HttpRequest) =
    response_f status_code reason_phrase (
      fun r -> async {
        if content.Length > 0 then
          do! async_writeln r.Stream (sprintf "Content-Length: %d" content.Length)

        do! async_writeln r.Stream ""

        if content.Length > 0 then
          do! r.Stream.WriteAsync(content, 0, content.Length) })
      request

  // modifiers

  let set_header key value (http_request : HttpRequest) =
    http_request.Response.Headers.Add(key, value)
    http_request

  let set_cookie cookie = set_header "Set-Cookie" cookie

  // filters/applicatives

  let url    s (x : HttpRequest) = if s = x.Url    then Some x else None

  let meth0d s (x : HttpRequest) = if s = x.Method then Some x else None
  
  let is_secure(x : HttpRequest) = if x.IsSecure   then Some x else None

  let url_regex s (x : HttpRequest) = if Regex.IsMatch(x.Url,s) then Some x else None

  // see http://www.w3.org/Protocols/rfc2616/rfc2616-sec9.html
  // see Http.fsi for documentation

  let GET     (x : HttpRequest) = meth0d "GET" x
  let POST    (x : HttpRequest) = meth0d "POST" x
  let DELETE  (x : HttpRequest) = meth0d "DELETE" x
  let PUT     (x : HttpRequest) = meth0d "PUT" x
  let HEAD    (x : HttpRequest) = meth0d "HEAD" x
  let CONNECT (x : HttpRequest) = meth0d "CONNECT" x
  let PATCH   (x : HttpRequest) = meth0d "PATCH" x
  let TRACE   (x : HttpRequest) = meth0d "TRACE" x
  let OPTIONS (x : HttpRequest) = meth0d "OPTIONS" x

  // TODO: let continue ... ?
  // TODO: let switching_protocols ... ?

  // also see: http://www.vinaysahni.com/best-practices-for-a-pragmatic-restful-api

  let ok s = response 200 "OK" s >> succeed

  let OK a = ok (bytes_utf8 a)

  let created s = response 201 "Created" s >> succeed

  let CREATED s = created (bytes_utf8 s)

  let accepted s = response 202 "Accepted" s >> succeed

  let ACCEPTED s = accepted (bytes_utf8 s)

  let no_content : HttpRequest -> Async<unit> option =
    response 204 "No Content" (Array.zeroCreate 0) >> succeed

  let NO_CONTENT = no_content

  // 3xx Redirects

  let moved_permanently location =
    set_header "Location" location
    >> response 301 "Moved Permanently" (Array.zeroCreate 0)
    >> succeed

  let MOVED_PERMANENTLY location = moved_permanently location

  let found location =
    set_header "Location" location
    >> response 302 "Found" (Array.zeroCreate 0)
    >> succeed

  let FOUND location = found location

  let redirect url =
    set_header "Location" url
    >> set_header "Content-Type" "text/html; charset=utf-8"
    >> response 302 "Found" (bytes_utf8 <| sprintf "<html>
  <body>
    <a href=\"%s\">Content Moved</a>
  </body>
</html>
" url)
    >> succeed

  let not_modified : HttpRequest -> Async<unit> option =
    response 304 "Not Modified" (Array.zeroCreate 0) >> succeed

  let NOT_MODIFIED : HttpRequest -> Async<unit> option =
    not_modified

  let bad_request s = response 400 "Bad Request" s >> succeed

  let BAD_REQUEST s = bad_request (bytes_utf8 s)

  // 401: see http://stackoverflow.com/questions/3297048/403-forbidden-vs-401-unauthorized-http-responses/12675357

  let unauthorized s =
    set_header "WWW-Authenticate" "Basic realm=\"protected\""
    >> response 401 "Unauthorized" s
    >> succeed

  let UNAUTHORIZED s = unauthorized (bytes_utf8 s)

  let challenge = UNAUTHORIZED "401 Unauthorized."

  let forbidden s = response 403 "Forbidden" s >> succeed

  let FORBIDDEN s = forbidden (bytes_utf8 s)

  let not_found s = response 404 "Not Found" s >> succeed

  let NOT_FOUND message = not_found (bytes_utf8 message)

  let method_not_allowed s = response 405 "Method Not Allowed" s >> succeed

  let METHOD_NOT_ALLOWED s = method_not_allowed (bytes_utf8 s)

  let not_acceptable s = response 406 "Not Acceptable" s >> succeed

  let NOT_ACCEPTABLE message = not_acceptable (bytes_utf8 message)

  let request_timeout = response 408 "Request Timeout" (Array.zeroCreate 0) >> succeed
  // all-caps req.timeout elided intentionally

  let conflict s = response 409 "Conflict" s >> succeed

  let CONFLICT message = conflict (bytes_utf8 message)

  let gone s = response 410 "Gone" s >> succeed

  let GONE s = gone (bytes_utf8 s)

  let unsupported_media_type s = response 415 "Unsupported Media Type" s >> succeed

  let UNSUPPORTED_MEDIA_TYPE s = unsupported_media_type (bytes_utf8 s)

  let unprocessable_entity s = response 422 "Unprocessable Entity" s >> succeed

  let UNPROCESSABLE_ENTITY s = unprocessable_entity (bytes_utf8 s)

  let precondition_required body = response 428 "Precondition Required" body >> succeed

  let PRECONDITION_REQUIRED body = precondition_required (bytes_utf8 body)

  let too_many_requests s = response 429 "Too Many Requests" s >> succeed

  let TOO_MANY_REQUESTS s = too_many_requests (bytes_utf8 s)

  let internal_error message = response 500 "Internal Error" message >> succeed

  let INTERNAL_ERROR a = internal_error (bytes_utf8 a)


  let mime_type = function
    | ".bmp" -> "image/bmp"
    | ".css" -> "text/css"
    | ".gif" -> "image/gif"
    | ".png" -> "image/png"
    | ".ico" -> "image/x-icon"
    | ".htm"
    | ".html" -> "text/html";
    | ".jpe"
    | ".jpeg"
    | ".jpg" -> "image/jpeg"
    | ".js"  -> "application/x-javascript"
    | ".exe" -> "application/exe"
    | _ -> "application/octet-stream"

  let set_mime_type t = set_header "Content-Type" t

  let send_file filename r =
    let write_file file (r : HttpRequest) = async {
      use fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read)

      if fs.Length > 0L then
        do! async_writeln r.Stream (sprintf "Content-Length: %d" fs.Length)

      do! async_writeln r.Stream ""

      if fs.Length > 0L then
        do! transfer r.Stream fs }

    async { do! response_f 200 "OK" (write_file filename) r } |> succeed

  let CACHE_CONTROL_MAX_AGE = 600

  let file filename =
    if File.Exists filename then
      let file_info = new FileInfo(filename)
      let send_it _ = 
        let mimes = mime_type (file_info.Extension)
        set_header "Cache-Control" (sprintf "max-age=%d" CACHE_CONTROL_MAX_AGE)
        >> set_header "Last-Modified" (file_info.LastAccessTimeUtc.ToString("R")) 
        >> set_header "Expires" (DateTime.UtcNow.AddSeconds(float(CACHE_CONTROL_MAX_AGE)).ToString("R")) 
        >> set_mime_type mimes 
        >> send_file (filename)
      warbler ( fun (r:HttpRequest) ->
        let modified_since = (r.Headers ? ``if-modified-since`` )
        match modified_since with
        | Some v -> let date = DateTime.Parse v
                    if file_info.LastWriteTime > date then send_it ()
                    else NOT_MODIFIED
        | None   -> send_it ())
    else
      never

  let local_file fileName = sprintf "%s%s" Environment.CurrentDirectory fileName

  let browse_file filename = file (local_file filename)

  let browse : WebPart = warbler (fun req -> file (local_file req.Url))

  type WebResult = Option<Async<unit>>

  let dir (req : HttpRequest) : WebResult =

    let url = req.Url

    let dirname = local_file url
    let result = new StringBuilder()

    let filesize  (x : FileSystemInfo) =
      if (x.Attributes ||| FileAttributes.Directory = FileAttributes.Directory) then
        String.Format("{0,-14}",System.Web.HttpUtility.HtmlEncode("<DIR>"))
      else
        String.Format("{0,14}", (new FileInfo(x.FullName)).Length)

    let formatdate (t:DateTime) =
      t.ToString("MM-dd-yy") + "  " + t.ToString("hh:mmtt")

    let buildLine (x : FileSystemInfo) =
      result.Append(x.LastWriteTime.ToString() + "       " + filesize(x) + " " + x.Name + "<br/>\n")
      |> ignore

    if Directory.Exists dirname then
      let di = new DirectoryInfo(dirname)
      (di.GetFileSystemInfos()) |> Array.sortBy (fun x -> x.Name) |> Array.iter buildLine
      ok (bytes (result.ToString())) req
    else fail

  let close_pipe (p : HttpRequest option) =
    match p with
    | Some(x) ->
      x.Stream.Flush()
      x.Stream.Close()
    | None -> ()

  let parse_authentication_token (token : string) =
    let parts = token.Split (' ')
    let enc = parts.[1].Trim()
    let decoded = decode_base64 enc
    let indexOfColon = decoded.IndexOf(':')
    (parts.[0].ToLower(), decoded.Substring(0,indexOfColon), decoded.Substring(indexOfColon+1))

  let authenticate_basic f (p : HttpRequest) =
    let headers = p.Headers
    if headers.ContainsKey("authorization") then
      let header = headers.["authorization"]
      let (typ,username,password) = parse_authentication_token header
      p.Username <- username
      p.Password <- password
      if (typ.Equals("basic")) && f p then
        fail
      else
        challenge p
    else
      challenge p

  let log (s : Stream) (http_request : HttpRequest) =
    let bytes = bytes (sprintf "%A\n" (http_request.Method, http_request.RemoteAddress, http_request.Url, http_request.Query, http_request.Form, http_request.Headers))
    s.Write(bytes, 0, bytes.Length)
    succeed http_request

  open Suave.Sscanf

  let url_scan (pf : PrintfFormat<_,_,_,_,'t>) (h : 't ->  WebPart) : WebPart =
    let t url = sscanf pf url

    let F (r:HttpRequest) =
      try
        let y = r.Url |> t |> h
        try y r with ex -> r |> INTERNAL_ERROR (ex.ToString())
      with _ -> fail
    F
