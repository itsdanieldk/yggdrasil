namespace Yggdrasil.Content

open System

type Page =
    { Id: string
      Title: string
      Description: string
      Heading: string
      Emoji: string option
      Body: string }

type Note =
    { Id: string
      Title: string
      Description: string
      Date: DateOnly
      UpdatedDate: DateOnly option
      Body: string
      ReadingTime: string
      Tags: string list
      Draft: bool
      Featured: bool }

type Project =
    { Id: string
      Title: string
      Description: string
      Date: DateOnly
      UpdatedDate: DateOnly option
      Body: string
      ReadingTime: string
      Tags: string list
      Draft: bool
      Featured: bool
      DemoUrl: string option
      RepoUrl: string option }

[<RequireQualifiedAccess>]
type FeedEntry =
    | Note of Note
    | Project of Project

    member this.Title =
        match this with
        | Note n -> n.Title
        | Project p -> p.Title

    member this.Description =
        match this with
        | Note n -> n.Description
        | Project p -> p.Description

    member this.Date =
        match this with
        | Note n -> n.Date
        | Project p -> p.Date

    member this.UpdatedDate =
        match this with
        | Note n -> n.UpdatedDate
        | Project p -> p.UpdatedDate

    member this.Body =
        match this with
        | Note n -> n.Body
        | Project p -> p.Body

    member this.ReadingTime =
        match this with
        | Note n -> n.ReadingTime
        | Project p -> p.ReadingTime

type Fragrance =
    { Id: string
      Name: string
      House: string
      Url: string
      Rating: float option
      Note: string option
      Concentration: string option
      Image: string
      Image2x: string
      Wishlist: bool
      Draft: bool }
