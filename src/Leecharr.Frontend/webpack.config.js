const path = require("path");
const HtmlWebpackPlugin = require("html-webpack-plugin");
const MiniCssExtractPlugin = require("mini-css-extract-plugin");
const CopyWebpackPlugin = require("copy-webpack-plugin");

module.exports = (env, argv) => {
  const isDev = argv.mode === "development";

  return {
    entry: "./src/index.tsx",
    output: {
      path: path.resolve(__dirname, "../NzbDrone.Host/wwwroot"),
      filename: isDev ? "[name].js" : "[name].[contenthash].js",
      publicPath: "auto",
      clean: true,
    },
    resolve: {
      extensions: [".tsx", ".ts", ".js"],
      alias: {
        "@": path.resolve(__dirname, "src"),
      },
    },
    module: {
      rules: [
        {
          test: /\.tsx?$/,
          use: {
            loader: "ts-loader",
            options: {
              transpileOnly: true,
            },
          },
          exclude: /node_modules/,
        },
        {
          test: /\.module\.css$/,
          use: [
            isDev ? "style-loader" : MiniCssExtractPlugin.loader,
            {
              loader: "css-loader",
              options: {
                modules: {
                  localIdentName: isDev
                    ? "[name]__[local]--[hash:base64:5]"
                    : "[hash:base64:8]",
                },
              },
            },
          ],
        },
        {
          test: /\.css$/,
          exclude: /\.module\.css$/,
          use: [
            isDev ? "style-loader" : MiniCssExtractPlugin.loader,
            "css-loader",
          ],
        },
      ],
    },
    plugins: [
      new HtmlWebpackPlugin({
        template: "./src/index.html",
      }),
      new CopyWebpackPlugin({
        patterns: [
          { from: "./src/assets/favicon.svg", to: "favicon.svg" },
          { from: "./src/assets/swagger-custom.css", to: "swagger-custom.css" },
        ],
      }),
      ...(!isDev
        ? [
            new MiniCssExtractPlugin({
              filename: "[name].[contenthash].css",
            }),
          ]
        : []),
    ],
    devServer: {
      port: 3000,
      hot: true,
      historyApiFallback: true,
      proxy: [
        {
          context: ["/api"],
          target: "http://localhost:7889",
          changeOrigin: true,
        },
        {
          context: ["/signalr"],
          target: "http://localhost:7889",
          changeOrigin: true,
          ws: true,
        },
      ],
    },
    devtool: isDev ? "eval-source-map" : "source-map",
  };
};
