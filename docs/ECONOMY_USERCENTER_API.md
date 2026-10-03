# Economy Usercenter API

The Economy module can publish the complete, trader-independent item market to the SCUM Usercenter. No Discord message is sent.

## Usercenter setup

Add a long random shared secret to the deployed Usercenter file `scum/private/.env`:

```dotenv
ECONOMY_PUSH_TOKEN=replace-with-a-long-random-secret
```

The endpoint stores its snapshot below `scum/private/economy_data` by default. An absolute directory outside the public web root can be configured instead:

```dotenv
ECONOMY_DATA_DIR=/absolute/private/path/economy_data
```

The web server needs write access to that directory.

## Recon Tool setup

In **Economy > Usercenter API**:

1. Enable the API transfer.
2. Enter `https://your-domain.example/scum/api/economy_push.php` as endpoint.
3. Enter the same value used for `ECONOMY_PUSH_TOKEN`.
4. Save the settings.

A successful EconomyOverride upload publishes the current market automatically. **Update homepage now** publishes the current calculation without uploading or restarting the SCUM server.

## Data flow and security

- The tool aggregates identical item codes across all traders.
- The API receives all changed items, prices, transaction counts, and trader counts.
- No Steam IDs or other player identifiers are transferred.
- The push endpoint requires the shared token and writes the JSON snapshot atomically.
- The read endpoint requires the existing Steam Usercenter session.
- The browser refreshes only the market data every 30 seconds; it does not reload the whole page.

The SCUM server still reads `EconomyOverride.json` only during a server restart. Publishing the homepage does not apply prices to the running game server.
