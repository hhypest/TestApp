namespace TestApp.Core.Events

open System
open System.Collections.Frozen
open System.Globalization
open System.Runtime.CompilerServices
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.Json.Serialization
open TestApp.Core.Events
open TestApp.Core.Types

type private MetaData =
    {
      Name: string
      PayloadName: string
      PayloadType: Type
      ToDomain: Guid -> DateTime -> obj -> DomainEvent
      FromDomain: DomainEvent -> struct (Guid * DateTime * obj) option
    }

module private SerializeUtils =
    let private all : FrozenDictionary<string, MetaData> =
        [|
            {
                Name = "TestCreated"
                PayloadName = "Test"
                PayloadType = typeof<TestData>
                ToDomain = fun id dt payload -> DomainEvent.TestCreated(id, dt, payload :?> TestData)
                FromDomain = function
                    | DomainEvent.TestCreated(id, dt, payload) -> Some(struct (id, dt, box payload))
                    | _ -> None
            }
            {
                Name = "AskCreated"
                PayloadName = "Ask"
                PayloadType = typeof<AskData>
                ToDomain = fun id dt payload -> DomainEvent.AskCreated(id, dt, payload :?> AskData)
                FromDomain = function
                    | DomainEvent.AskCreated(id, dt, payload) -> Some(struct (id, dt, box payload))
                    | _ -> None
            }
            {
                Name = "AnswerCreated"
                PayloadName = "Answer"
                PayloadType = typeof<AnswerData>
                ToDomain = fun id dt payload -> DomainEvent.AnswerCreated(id, dt, payload :?> AnswerData)
                FromDomain = function
                    | DomainEvent.AnswerCreated(id, dt, payload) -> Some(struct (id, dt, box payload))
                    | _ -> None
            }
        |]
        |> Seq.map (fun meta -> meta.Name, meta)
        |> dict
        |> fun d -> d.ToFrozenDictionary()

    [<MethodImpl(MethodImplOptions.AggressiveInlining)>]
    let tryFind(name: string) =
        match all.TryGetValue(name) with
        | true, meta -> ValueSome meta
        | _ -> ValueNone

    [<MethodImpl(MethodImplOptions.AggressiveInlining)>]
    let tryFindByDU(event: DomainEvent) =
        all.Values
        |> Seq.tryPick (fun meta ->
            match meta.FromDomain event with
            | Some v -> Some (meta, v)
            | None -> None)
        |> function
            | Some x -> ValueSome x
            | None -> ValueNone

type public DomainEventConverter() =
    inherit JsonConverter<DomainEvent>()

    [<Literal>]
    let EventId = "EventId"

    [<Literal>]
    let CreatedAt = "CreatedAt"

    override _.Write(writer: Utf8JsonWriter, value: DomainEvent, options: JsonSerializerOptions): unit =
        match SerializeUtils.tryFindByDU value with
        | ValueSome (meta, struct(eventId, createdAt, payload)) ->
            let eventObj = JsonObject()
            eventObj[EventId] <- JsonValue.Create(eventId.ToString())
            eventObj[CreatedAt] <- JsonValue.Create(createdAt.ToString("O", CultureInfo.InvariantCulture))
            eventObj[meta.PayloadName] <- JsonSerializer.SerializeToNode(payload, meta.PayloadType, options)
            let root = JsonObject()
            root[meta.Name] <- eventObj
            root.WriteTo(writer, options)
        | ValueNone ->
            raise (JsonException("Unknown DomainEvent for serialization."))

    override _.Read(reader: byref<Utf8JsonReader>, _: Type, options: JsonSerializerOptions): DomainEvent =
        let root = JsonNode.Parse(&reader) :?> JsonObject
        if root.Count <> 1 then
            raise (JsonException("Expected single top-level property for event type."))

        let eventType = Seq.exactlyOne root |> fun kv -> kv.Key

        match SerializeUtils.tryFind eventType with
        | ValueSome meta ->
            let eventContent =
                match root[eventType] with
                | :? JsonObject as obj -> obj
                | _ -> raise (JsonException("Event content is not a JsonObject."))

            let eventId = Guid.Parse(eventContent[EventId].GetValue<string>())
            let createdAt = DateTime.Parse(eventContent[CreatedAt].GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            let payload = eventContent[meta.PayloadName].Deserialize(meta.PayloadType, options)
            meta.ToDomain eventId createdAt payload
        | ValueNone ->
            raise (JsonException($"Unknown event type: {eventType}"))